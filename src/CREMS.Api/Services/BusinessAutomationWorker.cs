using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;
public sealed class BusinessAutomationWorker(IServiceScopeFactory scopes,ILogger<BusinessAutomationWorker> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{await Run(stoppingToken);}catch(Exception ex){logger.LogError(ex,"CREMS business automation cycle failed");}await Task.Delay(TimeSpan.FromMinutes(15),stoppingToken);}}
 private async Task Run(CancellationToken token){await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var now=DateTimeOffset.UtcNow;var expired=await db.SalesQuotes.Where(x=>x.ValidUntil<now&&(x.Status==QuoteStatus.Draft||x.Status==QuoteStatus.Sent||x.Status==QuoteStatus.Negotiating)).ToListAsync(token);foreach(var q in expired)q.Status=QuoteStatus.Expired;
 await MaintenanceRules.GeneratePreventiveJobsAsync(db, now, token);
 var delayed=await db.ApprovalRequests.Include(x=>x.StageDecisions).Where(x=>x.Status==ApprovalStatus.Pending).ToListAsync(token);foreach(var approval in delayed){var stage=approval.StageDecisions.FirstOrDefault(x=>x.StageNumber==approval.CurrentStage);if(stage is not null&&now-approval.CreatedAt>TimeSpan.FromHours(24)&&!await db.ManagementTasks.AnyAsync(x=>!x.IsCompleted&&x.SourceEntityType==nameof(ApprovalRequest)&&x.SourceEntityId==approval.Id,token))db.ManagementTasks.Add(new ManagementTask{BranchId=approval.BranchId,Category=TaskCategory.PendingApproval,Priority=TaskPriority.High,Title=$"Approval delayed: {approval.RequestNumber}",DueAt=now,SourceEntityType=nameof(ApprovalRequest),SourceEntityId=approval.Id});}await db.SaveChangesAsync(token);}
}
