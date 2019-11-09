using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Models;
using OrchardCore.Workflows.Abstractions.Models;
using OrchardCore.Workflows.Activities;
using OrchardCore.Workflows.Models;
using OrchardCore.Workflows.Services;

namespace OrchardCore.Tenants.Workflows.Activities
{
    public class CreateTenantTask : TenantTask
    {
        public CreateTenantTask(IShellSettingsManager shellSettingsManager, IShellHost shellHost, IWorkflowExpressionEvaluator expressionEvaluator, IWorkflowScriptEvaluator scriptEvaluator, IStringLocalizer<CreateTenantTask> localizer)
            : base(shellSettingsManager, shellHost, expressionEvaluator, scriptEvaluator, localizer)
        {
        }

        public override string Name => nameof(CreateTenantTask);
        public override LocalizedString DisplayText => T["Create Tenant Task"];

        public string ContentType
        {
            get => GetProperty<string>();
            set => SetProperty(value);
        }
        
        public WorkflowExpression<string> RequestUrlPrefix
        {
            get => GetProperty(() => new WorkflowExpression<string>());
            set => SetProperty(value);
        }

        public WorkflowExpression<string> RequestUrlHost
        {
            get => GetProperty(() => new WorkflowExpression<string>());
            set => SetProperty(value);
        }

        public WorkflowExpression<string> DatabaseProvider
        {
            get => GetProperty(() => new WorkflowExpression<string>());
            set => SetProperty(value);
        }

        public WorkflowExpression<string> ConnectionString
        {
            get => GetProperty(() => new WorkflowExpression<string>());
            set => SetProperty(value);
        }

        public WorkflowExpression<string> TablePrefix
        {
            get => GetProperty(() => new WorkflowExpression<string>());
            set => SetProperty(value);
        }

        public WorkflowExpression<string> RecipeName
        {
            get => GetProperty(() => new WorkflowExpression<string>());
            set => SetProperty(value);
        }
        
        public override IEnumerable<Outcome> GetPossibleOutcomes(WorkflowExecutionContext workflowContext, ActivityContext activityContext)
        {
            return Outcomes(T["Done"]);
        }

        public async override Task<ActivityExecutionResult> ExecuteAsync(WorkflowExecutionContext workflowContext, ActivityContext activityContext)
        {
            var tenantNameTask = ExpressionEvaluator.EvaluateAsync(TenantName, workflowContext);
            var requestUrlPrefixTask = ExpressionEvaluator.EvaluateAsync(RequestUrlPrefix, workflowContext);
            var requestUrlHostTask = ExpressionEvaluator.EvaluateAsync(RequestUrlHost, workflowContext);
            var databaseProviderTask = ExpressionEvaluator.EvaluateAsync(DatabaseProvider, workflowContext);
            var connectionStringTask = ExpressionEvaluator.EvaluateAsync(ConnectionString, workflowContext);
            var tablePrefixTask = ExpressionEvaluator.EvaluateAsync(TablePrefix, workflowContext);
            var recipeNameTask = ExpressionEvaluator.EvaluateAsync(RecipeName, workflowContext);

            await Task.WhenAll(tenantNameTask, requestUrlPrefixTask, requestUrlHostTask, databaseProviderTask, connectionStringTask, tablePrefixTask, recipeNameTask);

            var shellSettings = new ShellSettings();

            if (!string.IsNullOrWhiteSpace(tenantNameTask.Result))
            {
                shellSettings = new ShellSettings
                {
                    Name = tenantNameTask.Result?.Trim(),
                    RequestUrlPrefix = requestUrlPrefixTask.Result?.Trim(),
                    RequestUrlHost = requestUrlHostTask.Result?.Trim(),
                    State = TenantState.Uninitialized
                };
                shellSettings["ConnectionString"] = connectionStringTask.Result?.Trim();
                shellSettings["TablePrefix"] = tablePrefixTask.Result?.Trim();
                shellSettings["DatabaseProvider"] = databaseProviderTask.Result?.Trim();
                shellSettings["Secret"] = Guid.NewGuid().ToString();
                shellSettings["RecipeName"] = recipeNameTask.Result.Trim();

                ShellSettingsManager.SaveSettings(shellSettings);
                await ShellHost.UpdateShellSettingsAsync(shellSettings);
            }

            workflowContext.LastResult = shellSettings;
            workflowContext.CorrelationId = shellSettings.Name;

            return Outcomes("Done");
        }
    }
}