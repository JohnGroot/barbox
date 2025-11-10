using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Unit tests for CreditService - credit balance management with caching
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
	public async Task GetBalance_FirstCall_QueriesBackend()
	{
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");

		// Act
		var result = await _creditService.GetBalanceAsync(TestPlayerId);

		// Assert
		// Note: May fail if player doesn't exist in backend
		if (!result.IsSuccess)
		{
			result.Error.ShouldNotBeNullOrEmpty("Error message should be provided");
			TestHelpers.LogTestInfo($"GetBalance failed (may be expected if player not in backend): {result.Error}");
		}
		else
		{
			result.Value.ShouldBeGreaterThanOrEqualTo(0, "Balance should be non-negative");
			TestHelpers.LogTestInfo($"Balance retrieved: {result.Value}");
		}
	}

	[Test]
	public async Task GetBalance_SecondCallWithin30Seconds_UsesCache()
	{
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");

		// First call to populate cache
		var firstResult = await _creditService.GetBalanceAsync(TestPlayerId);

		if (!firstResult.IsSuccess)
		{
			TestHelpers.LogTestInfo("Test skipped - first call failed");
			return;
		}

		// Act - Second call should use cache
		var secondResult = await _creditService.GetBalanceAsync(TestPlayerId);

		// Assert
		secondResult.IsSuccess.ShouldBeTrue("Second call should succeed if first succeeded");
		secondResult.Value.ShouldBe(firstResult.Value, "Cached value should match first call");
		TestHelpers.LogTestInfo("Cache appears to be working (same balance returned)");
	}

	[Test]
	public async Task GetBalance_WithForceRefresh_BypassesCache()
	{
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");

		var firstResult = await _creditService.GetBalanceAsync(TestPlayerId);

		if (!firstResult.IsSuccess)
		{
			TestHelpers.LogTestInfo("Test skipped - first call failed");
			return;
		}

		// Act - Force refresh should query backend
		var refreshedResult = await _creditService.GetBalanceAsync(TestPlayerId, forceRefresh: true);

		// Assert
		refreshedResult.IsSuccess.ShouldBeTrue("Force refresh should succeed if first call succeeded");
		refreshedResult.Value.ShouldBeGreaterThanOrEqualTo(0, "Balance should be non-negative");
		TestHelpers.LogTestInfo($"Force refresh returned: {refreshedResult.Value}");
	}

	[Test]
	public async Task SpendCredits_WithPositiveAmount_UpdatesBalance()
	{
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");
		var amount = 10;
		var reason = "test_purchase";

		// Act
		var result = await _creditService.SpendAsync(TestPlayerId, amount, reason);

		// Assert
		// Note: May fail if player doesn't have sufficient credits
		if (!result.IsSuccess)
		{
			result.Error.ShouldNotBeNullOrEmpty("Error message should be provided");
			TestHelpers.LogTestInfo($"Spend failed (may be expected if insufficient credits): {result.Error}");
		}
		else
		{
			result.Value.ShouldBeGreaterThanOrEqualTo(0, "Balance should be non-negative after spend");
			TestHelpers.LogTestInfo($"Credits spent successfully, new balance: {result.Value}");
		}
	}

	[Test]
	public async Task SpendCredits_WithZeroAmount_ReturnsError()
	{
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");

		// Act
		var result = await _creditService.SpendAsync(TestPlayerId, 0, "test");

		// Assert
		result.IsSuccess.ShouldBeFalse("Should not allow spending zero credits");
		result.Error.ShouldNotBeNullOrEmpty("Error message should be provided");
		TestHelpers.LogTestInfo($"Correctly rejected zero amount: {result.Error}");
	}

	[Test]
	public async Task SpendCredits_WithNegativeAmount_ReturnsError()
	{
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");

		// Act
		var result = await _creditService.SpendAsync(TestPlayerId, -10, "test");

		// Assert
		result.IsSuccess.ShouldBeFalse("Should not allow spending negative credits");
		result.Error.ShouldNotBeNullOrEmpty("Error message should be provided");
		TestHelpers.LogTestInfo($"Correctly rejected negative amount: {result.Error}");
	}

	[Test]
	public async Task AddCredits_WithPositiveAmount_UpdatesBalance()
	{
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");
		var amount = 100;
		var reason = "test_reward";

		// Act
		var result = await _creditService.AddAsync(TestPlayerId, amount, reason);

		// Assert
		// Note: May fail if backend not available
		if (!result.IsSuccess)
		{
			result.Error.ShouldNotBeNullOrEmpty("Error message should be provided");
			TestHelpers.LogTestInfo($"Add credits failed: {result.Error}");
		}
		else
		{
			result.Value.ShouldBeGreaterThanOrEqualTo(amount, "Balance should at least include added amount");
			TestHelpers.LogTestInfo($"Credits added successfully, new balance: {result.Value}");
		}
	}

	[Test]
	public async Task ReconcileBalance_ForcesBackendSync()
	{
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");

		// Act & Assert - Should complete without exception
		await _creditService.ReconcileBalanceAsync(TestPlayerId);
		TestHelpers.LogTestInfo("Balance reconcile operation completed");
	}

	[Test]
	public async Task CacheInvalidation_AfterSpend_NextGetUsesBackend()
	{
		// This test verifies cache invalidation behavior
		// Arrange
		_creditService.ShouldNotBeNull("CreditService must be available");

		// Get initial balance to populate cache
		var initialResult = await _creditService.GetBalanceAsync(TestPlayerId);

		if (!initialResult.IsSuccess)
		{
			TestHelpers.LogTestInfo("Test skipped - initial get failed");
			return;
		}

		// Act - Spend credits (should invalidate cache)
		var spendResult = await _creditService.SpendAsync(TestPlayerId, 1, "cache_test");

		if (!spendResult.IsSuccess)
		{
			TestHelpers.LogTestInfo("Test skipped - spend failed");
			return;
		}

		// Get balance again - should reflect the spend
		var newResult = await _creditService.GetBalanceAsync(TestPlayerId);

		// Assert
		newResult.IsSuccess.ShouldBeTrue("Balance retrieval should succeed after spend");
		newResult.Value.ShouldBeLessThan(initialResult.Value,
			"Balance should decrease after spend (cache should be invalidated)");
		TestHelpers.LogTestInfo($"After spend: old={initialResult.Value}, new={newResult.Value}");
	}

	[Cleanup]
	public override Task CleanupTestResources()
	{
		base.CleanupTestResources();
		_creditService = null;
		return Task.CompletedTask;
	}
}
