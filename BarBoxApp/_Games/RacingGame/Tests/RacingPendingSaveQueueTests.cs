using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Unit tests for RacingPendingSaveQueue - the deferred-flush queue that keeps
/// lap-boundary backend saves off the frame that triggered them, while
/// guaranteeing they still fire on an early quit before the next flush.
/// </summary>
public class RacingPendingSaveQueueTests : TestClass
{
	private RacingPendingSaveQueue _queue;

	public RacingPendingSaveQueueTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public void Setup()
	{
		_queue = new RacingPendingSaveQueue();
	}

	[Test]
	public void Enqueue_DoesNotInvokeActionImmediately()
	{
		var invoked = false;

		_queue.Enqueue(() =>
		{
			invoked = true;
			return Task.CompletedTask;
		});

		invoked.ShouldBeFalse("Enqueue should only capture the action - it must not run until a flush happens");
		_queue.Count.ShouldBe(1);
	}

	[Test]
	public async Task FlushAsync_InvokesQueuedAction()
	{
		var invoked = false;

		_queue.Enqueue(() =>
		{
			invoked = true;
			return Task.CompletedTask;
		});

		await _queue.FlushAsync();

		invoked.ShouldBeTrue("Flushing should run every queued action");
		_queue.Count.ShouldBe(0);
	}

	[Test]
	public async Task FlushAsync_SimulatingEarlyQuitBeforeNextLap_StillPersistsQueuedLap()
	{
		// Reproduces the rage-quit hazard this queue exists to prevent:
		// lap 1 completes (enqueues a save) and the player quits before lap 2
		// ever starts (so nothing else would trigger a flush). Session
		// teardown must still flush the lap-1 save - simulated here by
		// calling FlushAsync with nothing further ever enqueued.
		var savedLapTimes = new System.Collections.Generic.List<float>();

		_queue.Enqueue(() =>
		{
			savedLapTimes.Add(12.345f); // lap 1's captured time
			return Task.CompletedTask;
		});

		// Player quits here - lap 2 never completes, so no further Enqueue call
		// ever happens. Teardown's flush is the only thing that can save lap 1.
		await _queue.FlushAsync();

		savedLapTimes.Count.ShouldBe(1, "Lap 1's save must persist even though the player quit before lap 2");
		savedLapTimes[0].ShouldBe(12.345f);
	}

	[Test]
	public async Task Flush_CalledTwice_DoesNotDoubleInvoke()
	{
		var invokeCount = 0;

		_queue.Enqueue(() =>
		{
			invokeCount++;
			return Task.CompletedTask;
		});

		// Simulates race completion flushing the queue, followed by a
		// teardown flush shortly after (e.g. player exits to menu right after
		// finishing) - the same queued save must not fire twice.
		await _queue.FlushAsync();
		await _queue.FlushAsync();

		invokeCount.ShouldBe(1, "A queued save must be started exactly once, no matter how many flush points observe it");
	}

	[Test]
	public async Task FlushAsync_WithMultiplePendingSaves_InvokesAllInOrder()
	{
		var order = new System.Collections.Generic.List<int>();

		_queue.Enqueue(() => { order.Add(1); return Task.CompletedTask; });
		_queue.Enqueue(() => { order.Add(2); return Task.CompletedTask; });
		_queue.Enqueue(() => { order.Add(3); return Task.CompletedTask; });

		await _queue.FlushAsync();

		order.Count.ShouldBe(3);
		order[0].ShouldBe(1);
		order[1].ShouldBe(2);
		order[2].ShouldBe(3);
	}

	[Test]
	public void FlushAsync_WithNothingQueued_IsNoOp()
	{
		_queue.Count.ShouldBe(0);

		var task = _queue.FlushAsync();

		task.IsCompleted.ShouldBeTrue("An empty flush should complete synchronously rather than allocate work");
	}
}
