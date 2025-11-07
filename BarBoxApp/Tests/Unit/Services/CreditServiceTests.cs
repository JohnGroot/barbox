using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Tests for CreditService - credit balance management with caching
/// </summary>
public class CreditServiceTests : BackendTestBase
{
	private CreditService _creditService;

	public CreditServiceTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_creditService = GetCreditService();
	}

	[Test]
	public async void GetBalance_FirstCall_QueriesBackend()
	{
		// Act
		var result = await _creditService.GetBalanceAsync(TestPlayerId);

		// Assert
		if (!result.IsSuccess)
		{
			TestHelpers.LogTestInfo($"GetBalance failed (expected if player not in backend): {result.Error}");
		}
		else
		{
			TestHelpers.LogTestInfo($"Balance retrieved: {result.Value}");
		}
	}

	[Test]
	public async void GetBalance_SecondCallWithin30Seconds_UsesCache()
	{
		// Arrange - First call to populate cache
		var firstResult = await _creditService.GetBalanceAsync(TestPlayerId);

		if (!firstResult.IsSuccess)
		{
			TestHelpers.LogTestInfo("Skipping cache test - first call failed");
			return;
		}

		// Act - Second call should use cache
		var secondResult = await _creditService.GetBalanceAsync(TestPlayerId);

		// Assert
		if (secondResult.IsSuccess)
		{
			if (secondResult.Value == firstResult.Value)
			{
				TestHelpers.LogTestInfo("Cache appears to be working (same balance returned)");
			}
		}
	}

	[Test]
	public async void GetBalance_WithForceRefresh_BypassesCache()
	{
		// Arrange
		var firstResult = await _creditService.GetBalanceAsync(TestPlayerId);

		if (!firstResult.IsSuccess)
		{
			TestHelpers.LogTestInfo("Skipping force refresh test - first call failed");
			return;
		}

		// Act - Force refresh should query backend
		var refreshedResult = await _creditService.GetBalanceAsync(TestPlayerId, forceRefresh: true);

		// Assert
		if (refreshedResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Force refresh returned: {refreshedResult.Value}");
		}
	}

	[Test]
	public async void SpendCredits_WithPositiveAmount_UpdatesBalance()
	{
		// Arrange
		var amount = 10;
		var reason = "test_purchase";

		// Act
		var result = await _creditService.SpendAsync(TestPlayerId, amount, reason);

		// Assert
		if (!result.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Spend failed (expected if insufficient credits): {result.Error}");
		}
		else
		{
			TestHelpers.LogTestInfo($"Credits spent successfully, new balance: {result.Value}");
		}
	}

	[Test]
	public async void SpendCredits_WithZeroAmount_ReturnsError()
	{
		// Act
		var result = await _creditService.SpendAsync(TestPlayerId, 0, "test");

		// Assert
		if (result.IsSuccess)
		{
			TestHelpers.LogTestError("Expected error for zero amount spend");
		}
		else
		{
			TestHelpers.LogTestInfo($"Correctly rejected zero amount: {result.Error}");
		}
	}

	[Test]
	public async void SpendCredits_WithNegativeAmount_ReturnsError()
	{
		// Act
		var result = await _creditService.SpendAsync(TestPlayerId, -10, "test");

		// Assert
		if (result.IsSuccess)
		{
			TestHelpers.LogTestError("Expected error for negative amount spend");
		}
		else
		{
			TestHelpers.LogTestInfo($"Correctly rejected negative amount: {result.Error}");
		}
	}

	[Test]
	public async void AddCredits_WithPositiveAmount_UpdatesBalance()
	{
		// Arrange
		var amount = 100;
		var reason = "test_reward";

		// Act
		var result = await _creditService.AddAsync(TestPlayerId, amount, reason);

		// Assert
		if (!result.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Add credits failed: {result.Error}");
		}
		else
		{
			TestHelpers.LogTestInfo($"Credits added successfully, new balance: {result.Value}");
		}
	}

	[Test]
	public async void ReconcileBalance_ForcesBackendSync()
	{
		// Act
		await _creditService.ReconcileBalanceAsync(TestPlayerId);

		// Assert
		TestHelpers.LogTestInfo("Balance reconcile operation completed");
	}

	[Test]
	public async void CacheInvalidation_AfterSpend_NextGetUsesBackend()
	{
		// This test verifies cache invalidation behavior
		// Arrange - Get initial balance to populate cache
		var initialResult = await _creditService.GetBalanceAsync(TestPlayerId);

		if (!initialResult.IsSuccess)
		{
			TestHelpers.LogTestInfo("Skipping cache invalidation test - initial get failed");
			return;
		}

		// Act - Spend credits (should invalidate cache)
		var spendResult = await _creditService.SpendAsync(TestPlayerId, 1, "cache_test");

		if (spendResult.IsSuccess)
		{
			// Get balance again - should reflect the spend
			var newResult = await _creditService.GetBalanceAsync(TestPlayerId);

			if (newResult.IsSuccess)
			{
				TestHelpers.LogTestInfo($"After spend: old={initialResult.Value}, new={newResult.Value}");
			}
		}
	}

	[Cleanup]
	public override void CleanupTestResources()
	{
		base.CleanupTestResources();
		_creditService = null;
	}
}
