using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeiYiJia.Abp.Workflow.Interface;
using MeiYiJia.Abp.Workflow.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sample.Abp.Workflow.Step;

namespace Sample.Abp.Workflow
{
    public class AppHostedService: BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly IWorkflowController _workflowController;
        private readonly IWorkHost _workHost;

        public AppHostedService(ILogger<AppHostedService> logger, 
            IWorkflowController workflowController,
            IWorkflowRegistry workflowRegistry,
            IWorkHost workHost)
        {
            _logger = logger;
            _workflowController = workflowController;
            _workflowRegistry = workflowRegistry;
            _workHost = workHost;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // throw new System.NotImplementedException();
            _workflowRegistry.RegisterWorkflow(new WorkflowDefinition()
            {
                Id = "test",
                Steps = new List<WorkFlowStep>()
                {
                    new WorkFlowStep()
                    {
                        Id = "No1",
                        StepType = typeof(FirstStepAsync)
                    },
                    // new WorkFlowStep()
                    // {
                    //     Id = "No2",
                    //     StepType = typeof(SecondStepAsync)
                    // },
                    // new WorkFlowStep()
                    // {
                    //     Id = "No3",
                    //     StepType = typeof(ThirdStepAsync)
                    // }
                }
            });
            for (var i = 0; i < 15; i++)
            {
                await _workflowController.StartWorkflow("test", new {TaskId = i});
            }
            await _workHost.StartAsync(stoppingToken);
        }
    }
}