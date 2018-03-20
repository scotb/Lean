﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Alphas.Analysis;
using QuantConnect.Algorithm.Framework.Alphas.Analysis.Providers;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.Alpha;
using QuantConnect.Logging;
using QuantConnect.Packets;

namespace QuantConnect.Lean.Engine.Alphas
{
    /// <summary>
    /// Default alpha handler that supports sending insights to the messaging handler, analyzing insights online
    /// </summary>
    public class DefaultAlphaHandler : IAlphaHandler
    {
        private DateTime _nextMessagingUpdate;
        private DateTime _nextPersistenceUpdate;
        private DateTime _lastSecurityValuesSnapshotTime;

        private bool _isNotFrameworkAlgorithm;
        private IMessagingHandler _messagingHandler;
        private ChartingInsightManagerExtension _charting;
        private ISecurityValuesProvider _securityValuesProvider;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentQueue<Packet> _messages = new ConcurrentQueue<Packet>();
        private readonly ConcurrentQueue<InsightQueueItem> _insightQueue = new ConcurrentQueue<InsightQueueItem>();

        /// <summary>
        /// Gets a flag indicating if this handler's thread is still running and processing messages
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Gets the current alpha runtime statistics
        /// </summary>
        public AlphaRuntimeStatistics RuntimeStatistics { get; private set; }

        /// <summary>
        /// Gets the algorithm's unique identifier
        /// </summary>
        protected string AlgorithmId => Job.AlgorithmId;

        /// <summary>
        /// Gets whether or not the job is a live job
        /// </summary>
        protected bool LiveMode => Job is LiveNodePacket;

        /// <summary>
        /// Gets the algorithm job packet
        /// </summary>
        protected AlgorithmNodePacket Job { get; private set; }

        /// <summary>
        /// Gets the algorithm instance
        /// </summary>
        protected IAlgorithm Algorithm { get; private set; }

        /// <summary>
        /// Gets or sets the interval at which the insights are persisted
        /// </summary>
        protected TimeSpan PersistenceUpdateInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets the interval at which insight updates are sent to the messaging handler
        /// </summary>
        protected TimeSpan MessagingUpdateInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets the insight manager instance used to manage the analysis of algorithm insights
        /// </summary>
        protected InsightManager InsightManager { get; private set; }

        /// <summary>
        /// Initializes this alpha handler to accept insights from the specified algorithm
        /// </summary>
        /// <param name="job">The algorithm job</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="messagingHandler">Handler used for sending insights</param>
        /// <param name="api">Api instance</param>
        public virtual void Initialize(AlgorithmNodePacket job, IAlgorithm algorithm, IMessagingHandler messagingHandler, IApi api)
        {
            // initializing these properties just in case, doens't hurt to have them populated
            Job = job;
            Algorithm = algorithm;
            _messagingHandler = messagingHandler;
            _isNotFrameworkAlgorithm = !algorithm.IsFrameworkAlgorithm;
            if (_isNotFrameworkAlgorithm)
            {
                return;
            }


            _securityValuesProvider = new AlgorithmSecurityValuesProvider(algorithm);

            InsightManager = CreateInsightManager();

            var statistics = new StatisticsInsightManagerExtension();
            RuntimeStatistics = statistics.Statistics;
            InsightManager.AddExtension(statistics);
            _charting = new ChartingInsightManagerExtension(algorithm, statistics);
            InsightManager.AddExtension(_charting);

            // when insight is generated, take snapshot of securities and place in queue for insight manager to process on alpha thread
            algorithm.InsightsGenerated += (algo, collection) => _insightQueue.Enqueue(new InsightQueueItem(collection.DateTimeUtc, CreateSecurityValuesSnapshot(), collection));
        }

        /// <summary>
        /// Invoked after the algorithm's Initialize method was called allowing the alpha handler to check
        /// other things, such as sampling period for backtests
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        public void OnAfterAlgorithmInitialized(IAlgorithm algorithm)
        {
            if (_isNotFrameworkAlgorithm)
            {
                return;
            }

            // send date ranges to extensions for initialization -- this data wasn't available when the handler was
            // initialzied, so we need to invoke it here
            InsightManager.InitializeExtensionsForRange(algorithm.StartDate, algorithm.EndDate, algorithm.UtcTime);
        }

        /// <summary>
        /// Performs processing in sync with the algorithm's time loop to provide consisten reading of data
        /// </summary>
        public virtual void ProcessSynchronousEvents()
        {
            if (_isNotFrameworkAlgorithm)
            {
                return;
            }

            // check the last snap shot time, we may have already produced a snapshot via OnInsightssGenerated
            if (_lastSecurityValuesSnapshotTime != Algorithm.UtcTime)
            {
                _insightQueue.Enqueue(new InsightQueueItem(Algorithm.UtcTime, CreateSecurityValuesSnapshot()));
            }
        }

        /// <summary>
        /// Thread entry point for asynchronous processing
        /// </summary>
        public virtual void Run()
        {
            if (_isNotFrameworkAlgorithm)
            {
                return;
            }

            IsActive = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // run main loop until canceled, will clean out work queues separately
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    ProcessAsynchronousEvents();
                }
                catch (Exception err)
                {
                    Log.Error(err);
                    throw;
                }

                Thread.Sleep(1);
            }

            // finish insight scoring analysis
            _insightQueue.ProcessUntilEmpty(item => InsightManager.Step(item.FrontierTimeUtc, item.SecurityValues, item.GeneratedInsights));

            // send final insight scoring updates before we exit
            var insights = InsightManager.GetUpdatedContexts().Select(context => context.Insight).ToList();
            _messages.Enqueue(new AlphaResultPacket(AlgorithmId, Job.UserId, insights));

            // finish sending packets
            _messages.ProcessUntilEmpty(packet => _messagingHandler.Send(packet));

            // persist insights at exit
            StoreInsights();

            Log.Trace("DefaultAlphaHandler.Run(): Ending Thread...");
            IsActive = false;
        }

        /// <summary>
        /// Stops processing in the <see cref="IAlphaHandler.Run"/> method
        /// </summary>
        public void Exit()
        {
            if (_isNotFrameworkAlgorithm)
            {
                return;
            }

            Log.Trace("DefaultAlphaHandler.Run(): Exiting Thread...");

            _cancellationTokenSource.Cancel(false);
        }

        /// <summary>
        /// Performs asynchronous processing, including broadcasting of insights to messaging handler
        /// </summary>
        protected void ProcessAsynchronousEvents()
        {
            // step the insight manager forward in time
            InsightQueueItem item;
            while (_insightQueue.TryDequeue(out item))
            {
                InsightManager.Step(item.FrontierTimeUtc, item.SecurityValues, item.GeneratedInsights);
            }

            // send insight upate messages
            Packet packet;
            while (_messages.TryDequeue(out packet))
            {
                _messagingHandler.Send(packet);
            }

            // persist generated insights to storage
            if (DateTime.UtcNow > _nextPersistenceUpdate)
            {
                StoreInsights();
                _nextPersistenceUpdate = DateTime.UtcNow + PersistenceUpdateInterval;
            }

            // push updated insights through messaging handler
            if (DateTime.UtcNow > _nextMessagingUpdate)
            {
                var list = InsightManager.GetUpdatedContexts().Select(context => context.Insight).ToList();
                if (list.Count > 0)
                {
                    _messages.Enqueue(new AlphaResultPacket
                    {
                        AlgorithmId = AlgorithmId,
                        Insights = list
                    });
                }
                _nextMessagingUpdate = DateTime.UtcNow + MessagingUpdateInterval;
            }
        }

        /// <summary>
        /// Save insight results to persistent storage
        /// </summary>
        protected virtual void StoreInsights()
        {
            // default save all results to disk and don't remove any from memory
            // this will result in one file with all of the insights/results in it
            var insights = InsightManager.AllInsights.OrderBy(insight => insight.GeneratedTimeUtc).ToList();
            if (insights.Count > 0)
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), AlgorithmId, "alpha-results.json");
                Directory.CreateDirectory(new FileInfo(path).DirectoryName);
                File.WriteAllText(path, JsonConvert.SerializeObject(insights, Formatting.Indented));
            }
        }

        /// <summary>
        /// Creates the <see cref="InsightManager"/> to manage the analysis of generated insights
        /// </summary>
        /// <returns>A new insight manager instance</returns>
        protected virtual InsightManager CreateInsightManager()
        {
            var scoreFunctionProvider = new DefaultInsightScoreFunctionProvider();
            return new InsightManager(scoreFunctionProvider, 0);
        }

        private ReadOnlySecurityValuesCollection CreateSecurityValuesSnapshot()
        {
            _lastSecurityValuesSnapshotTime = Algorithm.UtcTime;
            return _securityValuesProvider.GetValues(Algorithm.Securities.Keys);
        }

        class InsightQueueItem
        {
            public DateTime FrontierTimeUtc;
            public InsightCollection GeneratedInsights;
            public ReadOnlySecurityValuesCollection SecurityValues;

            public InsightQueueItem(DateTime frontierTimeUtc, ReadOnlySecurityValuesCollection securityValues, InsightCollection generatedInsights = null)
            {
                FrontierTimeUtc = frontierTimeUtc;
                SecurityValues = securityValues;
                GeneratedInsights = generatedInsights ?? new InsightCollection(frontierTimeUtc, Enumerable.Empty<Insight>());
            }
        }
    }
}