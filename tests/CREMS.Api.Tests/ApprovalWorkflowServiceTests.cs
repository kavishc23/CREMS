using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class ApprovalWorkflowServiceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-21T08:00:00+12:00");

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    public void Heavy_equipment_and_duration_greater_than_seven_days(int days, bool expected) =>
        Assert.Equal(expected, ApprovalWorkflowService.MatchesBooking(ItemRule(AssetType.HeavyEquipment, HireDurationOperator.GreaterThan, 7), Context(Item(AssetType.HeavyEquipment, days))));

    [Fact]
    public void Another_asset_type_uses_its_configured_duration() =>
        Assert.True(ApprovalWorkflowService.MatchesBooking(ItemRule(AssetType.PassengerVehicle, HireDurationOperator.GreaterThanOrEqual, 3), Context(Item(AssetType.PassengerVehicle, 3))));

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Duration_can_match_without_an_asset_type(int days, bool expected)
    {
        var rule = new ApprovalWorkflow { Name = "Long hire", Type = ApprovalType.Booking, HireDurationOperator = HireDurationOperator.GreaterThanOrEqual, HireDurationDays = 2 };
        Assert.Equal(expected, ApprovalWorkflowService.MatchesBooking(rule, Context(Item(AssetType.LightEquipment, days))));
    }

    [Fact]
    public void All_and_any_modes_have_distinct_behavior()
    {
        var all = ItemRule(AssetType.HeavyEquipment, HireDurationOperator.GreaterThan, 7);
        var any = ItemRule(AssetType.HeavyEquipment, HireDurationOperator.GreaterThan, 7); any.ConditionMatchMode = ApprovalConditionMatchMode.Any;
        var lightLongHire = Context(Item(AssetType.LightEquipment, 8));
        Assert.False(ApprovalWorkflowService.MatchesBooking(all, lightLongHire));
        Assert.True(ApprovalWorkflowService.MatchesBooking(any, lightLongHire));
    }

    [Fact]
    public void All_mode_requires_asset_type_and_duration_on_the_same_item()
    {
        var context = Context(Item(AssetType.HeavyEquipment, 1), Item(AssetType.LightEquipment, 8));
        Assert.False(ApprovalWorkflowService.MatchesBooking(ItemRule(AssetType.HeavyEquipment, HireDurationOperator.GreaterThan, 7), context));
    }

    [Fact]
    public void Duration_operators_respect_exact_24_hour_boundaries()
    {
        var exact = Context(Item(AssetType.PowerEquipment, 7));
        Assert.True(ApprovalWorkflowService.MatchesBooking(DurationRule(HireDurationOperator.GreaterThanOrEqual, 7), exact));
        Assert.True(ApprovalWorkflowService.MatchesBooking(DurationRule(HireDurationOperator.LessThanOrEqual, 7), exact));
        Assert.False(ApprovalWorkflowService.MatchesBooking(DurationRule(HireDurationOperator.GreaterThan, 7), exact));
        Assert.False(ApprovalWorkflowService.MatchesBooking(DurationRule(HireDurationOperator.LessThan, 7), exact));
    }

    [Fact]
    public void Existing_saved_rules_retain_original_or_behavior()
    {
        var legacy = new ApprovalWorkflow { Name = "Legacy", Type = ApprovalType.Booking, TriggerForEquipment = true, TriggerForPersonnel = true };
        Assert.Equal(ApprovalConditionMatchMode.Any, legacy.ConditionMatchMode);
        Assert.True(ApprovalWorkflowService.MatchesBooking(legacy, Context(Item(AssetType.PassengerVehicle, 1), isEquipment: true)));
        Assert.True(ApprovalWorkflowService.MatchesBooking(legacy, Context(Item(AssetType.PassengerVehicle, 1), hasPersonnel: true)));
    }

    [Fact]
    public void Order_400_item_rule_wins_over_general_equipment_in_same_scope()
    {
        var general = new ApprovalWorkflow { Name = "General equipment", Type = ApprovalType.Booking, TriggerForEquipment = true, Priority = 300 };
        var specific = ItemRule(AssetType.HeavyEquipment, HireDurationOperator.GreaterThan, 7); specific.Priority = 400;
        var match = ApprovalWorkflowService.SelectBookingMatch([general, specific], Context(Item(AssetType.HeavyEquipment, 8), isEquipment: true));
        Assert.Equal(specific.Id, match?.WorkflowId);
    }

    [Fact]
    public void Scope_precedence_remains_ahead_of_rule_order()
    {
        var branchId = Guid.NewGuid();
        var scoped = new ApprovalWorkflow { Name = "Scoped equipment", Type = ApprovalType.Booking, BranchId = branchId, TriggerForEquipment = true, Priority = 300 };
        var global = ItemRule(AssetType.HeavyEquipment, HireDurationOperator.GreaterThan, 7); global.Priority = 400;
        Assert.Equal(scoped.Id, ApprovalWorkflowService.SelectBookingMatch([global, scoped], Context(Item(AssetType.HeavyEquipment, 8), branchId, true))?.WorkflowId);
    }

    [Fact]
    public async Task Configurable_conditions_create_read_and_edit_through_persistence()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var rule = ItemRule(AssetType.CommercialVehicle, HireDurationOperator.LessThanOrEqual, 5);
        await using (var write = new ApplicationDbContext(options)) { write.ApprovalWorkflows.Add(rule); await write.SaveChangesAsync(TestContext.Current.CancellationToken); }
        await using (var edit = new ApplicationDbContext(options))
        {
            var stored = await edit.ApprovalWorkflows.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(AssetType.CommercialVehicle, stored.AssetTypeCondition);
            Assert.Equal(HireDurationOperator.LessThanOrEqual, stored.HireDurationOperator);
            Assert.Equal(5, stored.HireDurationDays);
            Assert.Equal(ApprovalConditionMatchMode.All, stored.ConditionMatchMode);
            stored.AssetTypeCondition = AssetType.PowerEquipment; stored.HireDurationOperator = HireDurationOperator.GreaterThan; stored.HireDurationDays = 10;
            await edit.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var read = new ApplicationDbContext(options);
        var updated = await read.ApprovalWorkflows.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssetType.PowerEquipment, updated.AssetTypeCondition);
        Assert.Equal(10, updated.HireDurationDays);
    }

    [Fact]
    public async Task Request_type_flags_persist_for_a_rule_that_applies_to_both()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var write = new ApplicationDbContext(options))
        {
            write.ApprovalWorkflows.Add(new ApprovalWorkflow { Name = "Both", Type = ApprovalType.Booking, AppliesToBooking = true, AppliesToQuotation = true, IsDefaultForBookings = true });
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var read = new ApplicationDbContext(options);
        var rule = await read.ApprovalWorkflows.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(rule.AppliesToBooking);
        Assert.True(rule.AppliesToQuotation);
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, true, true)]
    public async Task Rental_booking_quotation_and_both_are_evaluated_only_for_their_selected_request_types(
        bool appliesToBooking, bool appliesToQuotation, bool matchesBooking, bool matchesQuotation)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var write = new ApplicationDbContext(options))
        {
            write.ApprovalWorkflows.Add(new ApprovalWorkflow
            {
                Name = "Request type rule",
                Type = ApprovalType.Booking,
                AppliesToBooking = appliesToBooking,
                AppliesToQuotation = appliesToQuotation,
                IsDefaultForBookings = true,
            });
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = new ApplicationDbContext(options);
        var context = Context(Item(AssetType.LightEquipment, 1));
        Assert.Equal(matchesBooking, await ApprovalWorkflowService.MatchBookingAsync(read, context, TestContext.Current.CancellationToken) is not null);
        Assert.Equal(matchesQuotation, await ApprovalWorkflowService.MatchBookingAsync(read, context, TestContext.Current.CancellationToken, forQuotation: true) is not null);
    }

    [Fact]
    public void Validation_accepts_exactly_the_three_supported_request_type_combinations()
    {
        Assert.DoesNotContain("requestTypes", ApprovalWorkflowsController.ValidateConditions(Request(HireDurationOperator.GreaterThan, 7) with { AppliesToBooking = true, AppliesToQuotation = false }).Keys);
        Assert.DoesNotContain("requestTypes", ApprovalWorkflowsController.ValidateConditions(Request(HireDurationOperator.GreaterThan, 7) with { AppliesToBooking = false, AppliesToQuotation = true }).Keys);
        Assert.DoesNotContain("requestTypes", ApprovalWorkflowsController.ValidateConditions(Request(HireDurationOperator.GreaterThan, 7) with { AppliesToBooking = true, AppliesToQuotation = true }).Keys);
        Assert.Contains("requestTypes", ApprovalWorkflowsController.ValidateConditions(Request(HireDurationOperator.GreaterThan, 7) with { AppliesToBooking = false, AppliesToQuotation = false }).Keys);
    }

    [Fact]
    public void Validation_rejects_incomplete_duration_and_accepts_duration_alone()
    {
        Assert.Contains("hireDuration", ApprovalWorkflowsController.ValidateConditions(Request(HireDurationOperator.GreaterThan, null)).Keys);
        Assert.Empty(ApprovalWorkflowsController.ValidateConditions(Request(HireDurationOperator.GreaterThan, 7)));
    }

    [Fact]
    public void Matching_rule_records_reason_and_rental_officer_to_manager_route()
    {
        var rule = ItemRule(AssetType.HeavyEquipment, HireDurationOperator.GreaterThan, 7);
        rule.Stages = [new() { Sequence = 1, Name = "Rental officer review", AssignedRole = SystemRoles.RentalOfficer }, new() { Sequence = 2, Name = "Branch manager approval", AssignedRole = SystemRoles.BranchManager }];
        var match = ApprovalWorkflowService.SelectBookingMatch([rule], Context(Item(AssetType.HeavyEquipment, 8)))!;
        var request = new ApprovalRequest { RequestNumber = "APR-TEST", EntityType = "Booking", Reason = $"BK-TEST: {match.Reason}" };
        ApprovalWorkflowService.ConfigureFromMatch(request, match); ApprovalWorkflowService.RecordRentalOfficerReview(request, Guid.NewGuid());
        Assert.Equal(rule.Id, request.WorkflowId);
        Assert.Contains("asset type is Heavy Equipment AND item hire duration > 7 days", request.Reason);
        Assert.Equal(2, request.CurrentStage);
        Assert.Equal(ApprovalStatus.Approved, request.StageDecisions.Single(x => x.StageNumber == 1).Status);
        Assert.Equal(SystemRoles.BranchManager, request.StageDecisions.Single(x => x.StageNumber == 2).AssignedRole);
        Assert.Equal(ApprovalStatus.Pending, request.Status);
    }

    private static ApprovalWorkflow ItemRule(AssetType type, HireDurationOperator op, decimal days) => new() { Name = "Item rule", Type = ApprovalType.Booking, AssetTypeCondition = type, HireDurationOperator = op, HireDurationDays = days, ConditionMatchMode = ApprovalConditionMatchMode.All };
    private static ApprovalWorkflow DurationRule(HireDurationOperator op, decimal days) => new() { Name = "Duration", Type = ApprovalType.Booking, HireDurationOperator = op, HireDurationDays = days };
    private static ApprovalWorkflowService.BookingApprovalItem Item(AssetType type, int days) => new(type, Start, Start.AddDays(days));
    private static ApprovalWorkflowService.BookingApprovalContext Context(ApprovalWorkflowService.BookingApprovalItem item, Guid? branchId = null, bool isEquipment = false, bool hasPersonnel = false) => Context([item], branchId, isEquipment, hasPersonnel);
    private static ApprovalWorkflowService.BookingApprovalContext Context(ApprovalWorkflowService.BookingApprovalItem first, ApprovalWorkflowService.BookingApprovalItem second) => Context([first, second]);
    private static ApprovalWorkflowService.BookingApprovalContext Context(IReadOnlyList<ApprovalWorkflowService.BookingApprovalItem> items, Guid? branchId = null, bool isEquipment = false, bool hasPersonnel = false) => new(branchId ?? Guid.NewGuid(), null, 1000m, isEquipment, hasPersonnel, false, items);
    private static SaveApprovalWorkflowRequest Request(HireDurationOperator? op, decimal? days) => new("Test", ApprovalType.Booking, "Booking", null, null, true, [new(1, "Rental officer review", SystemRoles.RentalOfficer, null)], HireDurationOperator: op, HireDurationDays: days);
}
