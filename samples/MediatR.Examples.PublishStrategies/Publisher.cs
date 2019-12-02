using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediatR.Examples.PublishStrategies
{
    public class Publisher
    {
        private readonly ServiceFactory _serviceFactory;

        public Publisher(ServiceFactory serviceFactory)
        {
            _serviceFactory = serviceFactory;

            PublishStrategies[PublishStrategy.Async] = new CustomMediator(_serviceFactory, AsyncContinueOnException);
            PublishStrategies[PublishStrategy.ParallelNoWait] = new CustomMediator(_serviceFactory, ParallelNoWait);
            PublishStrategies[PublishStrategy.ParallelWhenAll] = new CustomMediator(_serviceFactory, ParallelWhenAll);
            PublishStrategies[PublishStrategy.ParallelWhenAny] = new CustomMediator(_serviceFactory, ParallelWhenAny);
            PublishStrategies[PublishStrategy.SyncContinueOnException] = new CustomMediator(_serviceFactory, SyncContinueOnException);
            PublishStrategies[PublishStrategy.SyncStopOnException] = new CustomMediator(_serviceFactory, SyncStopOnException);
        }

        public IDictionary<PublishStrategy, IMediator> PublishStrategies = new Dictionary<PublishStrategy, IMediator>();
        public PublishStrategy DefaultStrategy { get; set; } = PublishStrategy.SyncContinueOnException;

        public Task Publish<TNotification>(TNotification notification)
        {
            return Publish(notification, DefaultStrategy, default(CancellationToken));
        }

        public Task Publish<TNotification>(TNotification notification, PublishStrategy strategy)
        {
            return Publish(notification, strategy, default(CancellationToken));
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken)
        {
            return Publish(notification, DefaultStrategy, cancellationToken);
        }

        public Task Publish<TNotification>(TNotification notification, PublishStrategy strategy, CancellationToken cancellationToken)
        {
            if (!PublishStrategies.TryGetValue(strategy, out var mediator))
            {
                throw new ArgumentException($"Unknown strategy: {strategy}");
            }

            return mediator.Publish(notification, cancellationToken);
        }

        private Task ParallelWhenAll(IEnumerable<Func<INotification, CancellationToken, Task>> handlers, INotification notification, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();

            foreach (var handler in handlers)
            {
                tasks.Add(Task.Run(() => handler(notification, cancellationToken)));
            }

            return Task.WhenAll(tasks);
        }

        private Task ParallelWhenAny(IEnumerable<Func<INotification, CancellationToken, Task>> handlers, INotification notification, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();

            foreach (var handler in handlers)
            {
                tasks.Add(Task.Run(() => handler(notification, cancellationToken)));
            }

            return Task.WhenAny(tasks);
        }

        private Task ParallelNoWait(IEnumerable<Func<INotification, CancellationToken, Task>> handlers, INotification notification, CancellationToken cancellationToken)
        {
            foreach (var handler in handlers)
            {
                Task.Run(() => handler(notification, cancellationToken))
                    .ContinueWith( (t) =>
                        {
                            if (t.IsFaulted)
                            {
                                //log each exception stack trace!
                                foreach (var exc in ((AggregateException)t.Exception).Flatten().InnerExceptions)
                                {
                                    //Log(exc.ToString());
                                    Console.WriteLine(exc.ToString());
                                }
                            }
                        }, TaskContinuationOptions.OnlyOnFaulted
                    );
            }

            return Task.CompletedTask;
        }

        private async Task AsyncContinueOnException(IEnumerable<Func<INotification, CancellationToken, Task>> handlers, INotification notification, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            var exceptions = new List<Exception>();

            foreach (var handler in handlers)
            {
                try
                {
                    tasks.Add(handler(notification, cancellationToken));
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
                {
                    exceptions.Add(ex);
                }
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (AggregateException ex)
            {
                exceptions.AddRange(ex.Flatten().InnerExceptions);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
            {
                exceptions.Add(ex);
            }

            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }

        private async Task SyncStopOnException(IEnumerable<Func<INotification, CancellationToken, Task>> handlers, INotification notification, CancellationToken cancellationToken)
        {
            foreach (var handler in handlers)
            {
                await handler(notification, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SyncContinueOnException(IEnumerable<Func<INotification, CancellationToken, Task>> handlers, INotification notification, CancellationToken cancellationToken)
        {
            var exceptions = new List<Exception>();

            foreach (var handler in handlers)
            {
                try
                {
                    await handler(notification, cancellationToken).ConfigureAwait(false);
                }
                catch (AggregateException ex)
                {
                    exceptions.AddRange(ex.Flatten().InnerExceptions);
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
                {
                    exceptions.Add(ex);
                }
            }

            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }
    }
}
