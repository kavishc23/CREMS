using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class CustomersController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var query = db.Customers.AsNoTracking();
        if (!scope.IsAdministrator)
            query = query.Where(customer => db.Bookings.Any(booking =>
                booking.CustomerId == customer.Id && booking.BranchId == scope.BranchId));

        var customers = await query
            .OrderBy(customer => customer.Name)
            .Select(customer => new CustomerResponse(
                customer.Id, customer.CustomerNumber, customer.Type, customer.Name,
                customer.Email, customer.Phone, customer.Address, customer.IdentificationNumber,
                customer.IsBlocked, customer.IsActive))
            .ToListAsync(cancellationToken);
        return Ok(customers);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        SaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var customerNumber = request.CustomerNumber.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(customer => customer.CustomerNumber == customerNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.CustomerNumber), "A customer with this number already exists.");
            return ValidationProblem(ModelState);
        }

        var customer = new Customer
        {
            CustomerNumber = customerNumber,
            Type = request.Type,
            Name = request.Name.Trim(),
            Email = Normalize(request.Email)?.ToLowerInvariant(),
            Phone = Normalize(request.Phone),
            Address = Normalize(request.Address),
            IdentificationNumber = Normalize(request.IdentificationNumber),
            IsActive = true,
            IsBlocked = false,
        };
        db.Customers.Add(customer);
        AuditWriter.Record(db, scope, "Customer created", "Customer", customer.Id,
            $"{customer.CustomerNumber} — {customer.Name} was created.", scope.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetAll), ToResponse(customer));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id,
        SaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FindAsync([id], cancellationToken);
        if (customer is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator &&
            !await db.Bookings.AnyAsync(booking => booking.CustomerId == id &&
                booking.BranchId == scope.BranchId, cancellationToken))) return Forbid();

        var customerNumber = request.CustomerNumber.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(
            other => other.Id != id && other.CustomerNumber == customerNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.CustomerNumber), "A customer with this number already exists.");
            return ValidationProblem(ModelState);
        }

        customer.CustomerNumber = customerNumber;
        customer.Type = request.Type;
        customer.Name = request.Name.Trim();
        customer.Email = Normalize(request.Email)?.ToLowerInvariant();
        customer.Phone = Normalize(request.Phone);
        customer.Address = Normalize(request.Address);
        customer.IdentificationNumber = Normalize(request.IdentificationNumber);
        AuditWriter.Record(db, scope, "Customer updated", "Customer", customer.Id,
            $"{customer.CustomerNumber} details were updated.", scope.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(customer));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<CustomerResponse>> SetStatus(
        Guid id,
        SetCustomerStatusRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FindAsync([id], cancellationToken);
        if (customer is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator &&
            !await db.Bookings.AnyAsync(booking => booking.CustomerId == id &&
                booking.BranchId == scope.BranchId, cancellationToken))) return Forbid();
        customer.IsActive = request.IsActive;
        customer.IsBlocked = request.IsBlocked;
        AuditWriter.Record(db, scope, "Customer status changed", "Customer", customer.Id,
            $"{customer.CustomerNumber} active={request.IsActive}, blocked={request.IsBlocked}.", scope.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(customer));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id, customer.CustomerNumber, customer.Type, customer.Name,
        customer.Email, customer.Phone, customer.Address, customer.IdentificationNumber,
        customer.IsBlocked, customer.IsActive);
}

public sealed record SaveCustomerRequest(
    [Required, MaxLength(50)] string CustomerNumber,
    CustomerType Type,
    [Required, MaxLength(150)] string Name,
    [EmailAddress, MaxLength(254)] string? Email,
    [MaxLength(50)] string? Phone,
    [MaxLength(500)] string? Address,
    [MaxLength(100)] string? IdentificationNumber);

public sealed record SetCustomerStatusRequest(bool IsActive, bool IsBlocked);
public sealed record CustomerResponse(
    Guid Id, string CustomerNumber, CustomerType Type, string Name,
    string? Email, string? Phone, string? Address, string? IdentificationNumber,
    bool IsBlocked, bool IsActive);
