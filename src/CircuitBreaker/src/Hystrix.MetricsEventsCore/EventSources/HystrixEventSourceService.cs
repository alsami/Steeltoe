﻿// Copyright 2017 the original author or authors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.Extensions.Hosting;
using Steeltoe.CircuitBreaker.Hystrix.CircuitBreaker;
using Steeltoe.CircuitBreaker.Hystrix.Metric.Consumer;
using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Steeltoe.CircuitBreaker.Hystrix.Metric.Consumer.HystrixDashboardStream;

namespace Steeltoe.CircuitBreaker.Hystrix.MetricsEventsCore.EventSources
{
    public class HystrixEventSourceService : IHostedService
    {
        public HystrixDashboardStream Stream { get; }

        public HystrixEventSourceService(HystrixDashboardStream stream)
        {
            Stream = stream;
        }

        protected internal IDisposable SampleSubscription { get; set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Subscribe();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void Subscribe() => SampleSubscription = Stream.Observe()
                 .ObserveOn(NewThreadScheduler.Default)
                 .Subscribe(OnNext, ReSubscribeOnError, ReSubscribe);

        private void ReSubscribeOnError(Exception ex) => ReSubscribe();

        private void ReSubscribe()
        {
            if (SampleSubscription != null)
            {
                SampleSubscription.Dispose();
                SampleSubscription = null;
            }

            Subscribe();
        }

        private void OnNext(DashboardData dashboardData)
        {
            if (dashboardData != null)
            {
                try
                {
                    var dictionary = new Dictionary<string, string>();
                    foreach (HystrixCommandMetrics commandMetrics in dashboardData.CommandMetrics)
                    {
                        var circuitBreaker = HystrixCircuitBreakerFactory.GetInstance(commandMetrics.CommandKey);
                        var isOpen = circuitBreaker?.IsOpen;
                        IHystrixCommandOptions commandProperties = commandMetrics.Properties;

                        HystrixMetricsEventSource.EventLogger.CommandMetrics(
                            commandKey: commandMetrics.CommandKey.Name,
                            commandGroup: commandMetrics.CommandGroup.Name,
                            isCiruitBreakerOpen: isOpen.HasValue && isOpen.Value,
                            errorCount: commandMetrics.Healthcounts.ErrorCount,
                            requestCount: commandMetrics.Healthcounts.TotalRequests,
                            currentConcurrentExecutionCount: commandMetrics.CurrentConcurrentExecutionCount,
                            latencyExecute_mean: commandMetrics.ExecutionTimeMean,
                            latencyTotal_mean: commandMetrics.TotalTimeMean,
                            reportingHosts: 1, // this will get summed across all instances in a cluster
                            threadPool: commandMetrics.ThreadPoolKey.Name);
                    }

                    foreach (HystrixThreadPoolMetrics threadPoolMetrics in dashboardData.ThreadPoolMetrics)
                    {
                        HystrixMetricsEventSource.EventLogger.ThreadPoolMetrics(
                             threadpoolKey: threadPoolMetrics.ThreadPoolKey.Name,
                             cumulativeCountThreadsExecuted: threadPoolMetrics.CumulativeCountThreadsExecuted,
                             currentActiveCount: threadPoolMetrics.CurrentActiveCount,
                             currentCompletedTaskCount: threadPoolMetrics.CurrentCompletedTaskCount,
                             currentCorePoolSize: threadPoolMetrics.CurrentCorePoolSize,
                             currentLargestPoolSize: threadPoolMetrics.CurrentLargestPoolSize,
                             currentMaximumPoolSize: threadPoolMetrics.CurrentMaximumPoolSize,
                             currentPoolSize: threadPoolMetrics.CurrentPoolSize,
                             currentQueueSize: threadPoolMetrics.CurrentQueueSize,
                             currentTaskCount: threadPoolMetrics.CurrentTaskCount,
                             reportingHosts: 1); // this will get summed across all instances in a cluster
                    }

                    foreach (HystrixCollapserMetrics collapserMetrics in dashboardData.CollapserMetrics)
                    {
                        HystrixMetricsEventSource.EventLogger.CollapserMetrics(
                            collapserKey: collapserMetrics.CollapserKey.Name,
                            rollingCountRequestsBatched: collapserMetrics.GetRollingCount(CollapserEventType.ADDED_TO_BATCH),
                            rollingCountBatches: collapserMetrics.GetRollingCount(CollapserEventType.BATCH_EXECUTED),
                            rollingCountResponsesFromCache: collapserMetrics.GetRollingCount(CollapserEventType.RESPONSE_FROM_CACHE),
                            batchSize_mean: collapserMetrics.BatchSizeMean,
                            reportingHosts: 1); // this will get summed across all instances in a cluster
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }
    }
}
