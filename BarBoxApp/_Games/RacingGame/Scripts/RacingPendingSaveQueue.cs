using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BarBox.Games.Racing;

/// <summary>
/// Buffers backend-save calls captured at a lap boundary so their async chain
/// (JSON serialize, retry/backoff) doesn't run synchronously on the frame
/// that triggered them. Callers must capture everything a queued action
/// needs as a closure at enqueue time - the action may not run until a later
/// frame, by which point live game state (current lap, checkpoints) may have
/// moved on.
///
/// Flush is safe to call from multiple sites (per-frame draining, race
/// completion, session teardown): it drains and clears the queue before
/// invoking anything, so a queued save is started exactly once no matter how
/// many flush points happen to catch it.
/// </summary>
public class RacingPendingSaveQueue
{
	private readonly List<Func<Task>> _pending = new();

	public int Count => _pending.Count;

	/// <summary>
	/// Queue a save action to run on a later flush instead of immediately.
	/// </summary>
	public void Enqueue(Func<Task> saveAction)
	{
		if (saveAction != null)
		{
			_pending.Add(saveAction);
		}
	}

	/// <summary>
	/// Drains all queued saves and fires them off. No-op when nothing is
	/// queued, so it's cheap to call unconditionally every frame.
	/// </summary>
	public void Flush()
	{
		foreach (var action in Drain())
		{
			_ = action();
		}
	}

	/// <summary>
	/// Drains all queued saves and returns a task that completes once every
	/// one of them has finished. Production call sites use <see cref="Flush"/>
	/// (Godot lifecycle hooks here are void, not async) - this overload exists
	/// so tests can deterministically await a flush instead of polling.
	/// </summary>
	public Task FlushAsync()
	{
		var actions = Drain();
		if (actions.Length == 0)
		{
			return Task.CompletedTask;
		}

		var tasks = new Task[actions.Length];
		for (int i = 0; i < actions.Length; i++)
		{
			tasks[i] = actions[i]();
		}

		return Task.WhenAll(tasks);
	}

	private Func<Task>[] Drain()
	{
		if (_pending.Count == 0)
		{
			return [];
		}

		var actions = _pending.ToArray();
		_pending.Clear();
		return actions;
	}
}
