// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Its.Log.Instrumentation.Extensions;

namespace Its.Log.Instrumentation.UnitTests
{
    
    public class LogEntryDispatchQueue : AsyncDispatchQueue<LogEntry>
    {
        public LogEntryDispatchQueue(Func<LogEntry, Task> send, IObservable<LogEntry> logEvents = null) :
            base(send, logEvents ?? Log.Events())
        {
        }
    }

    public class TelemetryDispatchQueue : AsyncDispatchQueue<Telemetry>
    {
        public TelemetryDispatchQueue(Func<Telemetry, Task> send, IObservable<Telemetry> telemetryEvents = null) :
            base(send, telemetryEvents ?? Log.TelemetryEvents())
        {
        }
    }
    public abstract class AsyncDispatchQueue<T> : IDisposable
    {
        private readonly Func<T, Task> send;
        private readonly IDisposable subscription;
        private readonly BlockingCollection<T> blockingCollection;
        private readonly Thread dispatcherThread;

        protected AsyncDispatchQueue(Func<T, Task> send, IObservable<T> events)
        {
            if (send == null)
            {
                throw new ArgumentNullException(nameof(send));
            }
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            this.send = send;
            blockingCollection = new BlockingCollection<T>();
            subscription = events.Subscribe(new Observer(blockingCollection));

            dispatcherThread = new Thread(Send);
            dispatcherThread.Start();
        }

        private void Send()
        {
            while (true)
            {
                try
                {
                    var value = blockingCollection.Take();
                    send(value).Wait();
                }
                catch (InvalidOperationException)
                {
                    // collection is completed
                    break;
                }
            }
        }

        private async Task Drain()
        {
            while (!blockingCollection.IsEmpty())
            {
                await Task.Delay(50);
            }
        }

        public void Dispose()
        {
            blockingCollection.CompleteAdding();
            subscription.Dispose();
            Drain().Wait(TimeSpan.FromSeconds(30));
        }

        private class Observer : IObserver<T>
        {
            private readonly BlockingCollection<T> blockingCollection;

            public Observer(BlockingCollection<T> blockingCollection)
            {
                if (blockingCollection == null)
                {
                    throw new ArgumentNullException(nameof(blockingCollection));
                }
                this.blockingCollection = blockingCollection;
            }

            public void OnNext(T value) => blockingCollection.Add(value);

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }
}