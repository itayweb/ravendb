﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Util;
using Raven.Server.Documents.Operations;
using Raven.Server.Documents.PeriodicBackup;
using Raven.Server.NotificationCenter;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow.Utils;
using static Raven.Server.Utils.MetricCacher.Keys;

namespace Raven.Server.Documents.Handlers.Processors.OngoingTasks
{
    internal class OngoingTasksHandlerProcessorForBackupDatabaseOnce : AbstractOngoingTasksHandlerProcessorForBackupDatabaseOnce<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public OngoingTasksHandlerProcessorForBackupDatabaseOnce([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override void ScheduleBackup(BackupConfiguration backupConfiguration, long operationId, string backupName, Stopwatch sw, OperationCancelToken token)
        {
            var backupParameters = new BackupParameters
            {
                RetentionPolicy = null,
                StartTimeUtc = SystemTime.UtcNow,
                IsOneTimeBackup = true,
                BackupStatus = new PeriodicBackupStatus { TaskId = -1 },
                OperationId = operationId,
                BackupToLocalFolder = BackupConfiguration.CanBackupUsing(backupConfiguration.LocalSettings),
                IsFullBackup = true,
                TempBackupPath = BackupUtils.GetBackupTempPath(RequestHandler.Database.Configuration, "OneTimeBackupTemp"),
                Name = backupName
            };

            var backupTask = new BackupTask(RequestHandler.Database, backupParameters, backupConfiguration, Logger);
            var threadName = $"Backup thread {backupName} for database '{RequestHandler.Database.Name}'";

            var t = RequestHandler.Database.Operations.AddLocalOperation(
                operationId,
                OperationType.DatabaseBackup,
                $"Manual backup for database: {RequestHandler.Database.Name}",
                detailedDescription: null,
                onProgress =>
                {
                    var tcs = new TaskCompletionSource<IOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    PoolOfThreads.GlobalRavenThreadPool.LongRunning(x =>
                    {
                        try
                        {
                            ThreadHelper.TrySetThreadPriority(ThreadPriority.BelowNormal, threadName, Logger);
                            NativeMemory.EnsureRegistered();

                            using (RequestHandler.Database.PreventFromUnloadingByIdleOperations())
                            {
                                var runningBackupStatus = new PeriodicBackupStatus { TaskId = 0, BackupType = backupConfiguration.BackupType };
                                var backupResult = backupTask.RunPeriodicBackup(onProgress, ref runningBackupStatus);
                                BackupTask.SaveBackupStatus(runningBackupStatus, RequestHandler.Database, Logger, backupResult);
                                tcs.SetResult(backupResult);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            tcs.SetCanceled();
                        }
                        catch (Exception e)
                        {
                            if (Logger.IsOperationsEnabled)
                                Logger.Operations($"Failed to run the backup thread: '{backupName}'", e);

                            tcs.SetException(e);
                        }
                        finally
                        {
                            ServerStore.ConcurrentBackupsCounter.FinishBackup(backupName, backupStatus: null, sw.Elapsed, Logger);
                        }
                    }, null, threadName);
                    return tcs.Task;
                },
                token: token);

            var _ = t.ContinueWith(_ =>
            {
                token.Dispose();
            });
        }

        protected override long GetNextOperationId()
        {
            return RequestHandler.Database.Operations.GetNextOperationId();
        }

        protected override AbstractNotificationCenter GetNotificationCenter()
        {
            return RequestHandler.Database.NotificationCenter;
        }
    }
}