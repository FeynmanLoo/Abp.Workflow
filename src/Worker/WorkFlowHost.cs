using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeiYiJia.Abp.Workflow.Exception;
using MeiYiJia.Abp.Workflow.Interface;
using MeiYiJia.Abp.Workflow.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace MeiYiJia.Abp.Workflow.Worker
{
    public class WorkFlowHost: IWorkHost
    {
        private readonly IWorkflowController _workflowController;
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IPersistenceProvider _persistenceProvider;
        private readonly WorkflowOptions _options;
        
        public WorkFlowHost(
            IWorkflowController workflowController, 
            IPersistenceProvider persistenceProvider,
            ILogger<WorkFlowHost> logger, 
            IServiceProvider serviceProvider,
            IOptions<WorkflowOptions> options)
        {
            _workflowController = workflowController;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _persistenceProvider = persistenceProvider;
            _options = options.Value;
        }

        public async Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"{GetType().Name} is running !");
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(2000, stoppingToken);
                var wfiRunnable = await _persistenceProvider.GetRunnableInstanceAsync(stoppingToken);
                if (wfiRunnable != null)
                {
                    await _workflowController.ResumeWorkflow(wfiRunnable);
                }
                if (_workflowController.TryGetWorkflowInstance(out var wfi))
                {
                    AsyncContext.Run(async () =>
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var context = new StepExecutionContext()
                        {
                            StoppingToken = stoppingToken,
                            ServiceProvider =  _serviceProvider,
                            Logger = _logger,
                            ContextData = wfi.Data
                        };
                        var executionResult = new ExecutionResult();
                        var sw = new Stopwatch();
                        try
                        {
                            _options?.Start?.Invoke(scope.ServiceProvider, wfi);
                            var steps = scope.ServiceProvider.GetServices<IStepBodyAsync>().ToList();
                            _logger.LogInformation($"{wfi.Id} Begin");
                            sw.Start();
                            if (!steps.Any())
                            {
                                throw new WorkflowStepNotRegisteredException(wfi.WorkflowId, wfi.Version);
                            }
                            foreach (var workFlowStep in wfi.Steps)
                            {
                                var retryCnt = 0;
                                context.CurrentStep = workFlowStep;
                                var stepBody = steps.FirstOrDefault(m => m.GetType() == workFlowStep.StepType);
                                if (stepBody == null)
                                {
                                    throw new NullReferenceException(workFlowStep?.StepType?.FullName);
                                }
                                _logger.LogInformation($"{workFlowStep.Id}, Begin");
                                var inComeData = context.ContextData;
                                // retry step when fail
                                while (retryCnt++ <= Math.Abs(context.CurrentStep.FailedRetryCount))
                                {
                                    executionResult = await stepBody.RunAsync(context, stoppingToken);
                                    if (executionResult.Proceed)
                                    {
                                        break;
                                    }
                                }

                                var outComeData = context.ContextData;
                                // persistent current step data & status
                                await _persistenceProvider.PersistWorkflowStepAsync(wfi.Id, wfi.WorkflowId, workFlowStep, inComeData, outComeData, executionResult, stoppingToken);
                                if (!executionResult.Proceed)
                                {
                                    _logger.LogError(
                                        $"{workFlowStep.Id}, retryCount: {retryCnt - 1}, error：{executionResult.InnerException.Message}");
                                    break;
                                }

                                _logger.LogInformation($"{workFlowStep.Id}, End");
                                _logger.LogInformation(
                                    $"{workFlowStep.Id}, {executionResult.ConsumeElapsedMilliseconds} ms");
                            }

                            sw.Stop();
                            if (!executionResult.Proceed)
                            {
                                _logger.LogError($"{wfi.Id}, End With Exception: {executionResult.InnerException}");
                            }
                            _logger.LogInformation($"{wfi.Id}, End and Execute: {sw.ElapsedMilliseconds} ms");
                        }
                        catch (System.Exception e)
                        {
                            _logger.LogError(e.Message, e);
                            executionResult.Proceed = false;
                            executionResult.InnerException = e;
                        }
                        finally
                        {
                            await _persistenceProvider.PersistWorkflowInstanceAsync(wfi, context, executionResult, sw.ElapsedMilliseconds, stoppingToken);
                            _options?.End?.Invoke(scope.ServiceProvider, wfi, context, executionResult);
                        }
                    });
                }
            }
            _logger.LogInformation($"{nameof(GetType)} is stop !");
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }
}