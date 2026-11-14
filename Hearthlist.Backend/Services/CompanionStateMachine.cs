using System.Diagnostics;
using Hearthlist.Backend.Models;

namespace Hearthlist.Backend.Services;

/// <summary>
/// Drives the companion's emotional state machine.
///
/// Transitions:
///   Idle     --task completed--> Happy (brief) --> Idle
///   Idle     --N min no interaction--> Sleepy (until user interacts)
///   Idle     --app opened--> Greeting (brief) --> Idle
///
/// The renderer subscribes to <see cref="StateChanged"/> (or polls CurrentState)
/// to decide which animation to play.
///
/// Threading contract:
///  - All state reads/writes are guarded by <see cref="_lock"/>.
///  - <see cref="StateChanged"/> is ALWAYS raised OUTSIDE the lock, so
///    subscribers may safely call back into the machine (e.g. RegisterActivity)
///    without risking a self-deadlock.
///  - Timers use CancellationTokenSource cancellation; a monotonically
///    increasing <see cref="_generation"/> token prevents a stale timer from
///    overwriting a newer state.
///  - <see cref="StateChanged"/> may fire on a thread-pool thread (timers).
///    Hosts that update UI must marshal to their UI thread.
/// </summary>
public class CompanionStateMachine : IDisposable
{
    private readonly TimeSpan _happyDuration = TimeSpan.FromSeconds(3);
    private readonly TimeSpan _greetingDuration = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private CompanionState _state = CompanionState.Idle;
    private long _generation;
    private CancellationTokenSource? _transitionCts;
    private CancellationTokenSource? _sleepyCts;
    private TimeSpan _sleepyTimeout;

    /// <summary>Raised whenever the companion state changes. Renderer subscribes here.</summary>
    public event EventHandler<CompanionState>? StateChanged;

    public CompanionState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public CompanionStateMachine(TimeSpan sleepyTimeout)
    {
        _sleepyTimeout = sleepyTimeout;
    }

    public void SetSleepyTimeout(TimeSpan timeout)
    {
        lock (_lock)
        {
            _sleepyTimeout = timeout;
        }
        RestartSleepyTimer();
    }

    /// <summary>Call once when the app/editor panel opens.</summary>
    public void OnAppOpened()
    {
        SetState(CompanionState.Greeting);
        RestartSleepyTimer();
    }

    /// <summary>Call whenever the user interacts (any input), to reset the sleepy timer.</summary>
    public void RegisterActivity()
    {
        if (CurrentState == CompanionState.Sleepy)
            SetState(CompanionState.Idle);

        RestartSleepyTimer();
    }

    /// <summary>Triggers a brief happy state. Task completed externally.</summary>
    public void TriggerHappy()
    {
        SetState(CompanionState.Happy);
        RestartSleepyTimer();
    }

    private void SetState(CompanionState newState)
    {
        long myGeneration;
        CompanionState previous;
        CancellationToken revertToken;

        lock (_lock)
        {
            previous = _state;
            if (_state == newState)
                return;

            _state = newState;
            myGeneration = ++_generation;

            // Cancel any in-flight revert that set a transient state earlier.
            // NOTE: we only Cancel() and never Dispose() here — a background
            // RevertToIdleAfterAsync may still hold this token and awaiting a
            // disposed CTS throws ObjectDisposedException. GC collects the CTS.
            _transitionCts?.Cancel();
            _transitionCts = new CancellationTokenSource();
            revertToken = _transitionCts.Token;
        }

        Debug.WriteLine($"[Hearthlist] Companion state {previous} -> {newState}");

        // Raise OUTSIDE the lock so subscribers can call back into us freely.
        StateChanged?.Invoke(this, newState);

        // Transient happy/greeting states revert to Idle after their duration.
        if (newState is CompanionState.Happy or CompanionState.Greeting)
        {
            var delay = newState == CompanionState.Happy ? _happyDuration : _greetingDuration;
            var _ = RevertToIdleAfterAsync(delay, myGeneration, revertToken);
        }
    }

    private async Task RevertToIdleAfterAsync(TimeSpan delay, long expectedGeneration, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (TaskCanceledException)
        {
            return; // A newer transition superseded this one.
        }

        // Only revert if no newer state was applied while we waited.
        lock (_lock)
        {
            if (_generation != expectedGeneration)
                return;
        }

        SetState(CompanionState.Idle);
    }

    private void RestartSleepyTimer()
    {
        CancellationTokenSource cts;
        long expectedGeneration;

        lock (_lock)
        {
            expectedGeneration = _generation;

            // Cancel the previous timer so a stale delay cannot fire later.
            // Never dispose — a background ArmSleepyTimerAsync may still await it.
            _sleepyCts?.Cancel();
            _sleepyCts = null;

            cts = new CancellationTokenSource();
            _sleepyCts = cts;
        }

        var _ = ArmSleepyTimerAsync(_sleepyTimeout, expectedGeneration, cts.Token);
    }

    private async Task ArmSleepyTimerAsync(TimeSpan timeout, long expectedGeneration, CancellationToken token)
    {
        try
        {
            await Task.Delay(timeout, token);
        }
        catch (TaskCanceledException)
        {
            return; // Activity reset the timer.
        }

        bool goSleepy;
        lock (_lock)
        {
            goSleepy = _generation == expectedGeneration && _state == CompanionState.Idle;
        }

        if (goSleepy)
            SetState(CompanionState.Sleepy);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            // Cancel only — a background timer/revert may still hold these
            // tokens, and disposing while they await throws ObjectDisposedException.
            _transitionCts?.Cancel();
            _transitionCts = null;
            _sleepyCts?.Cancel();
            _sleepyCts = null;
        }
        GC.SuppressFinalize(this);
    }
}
