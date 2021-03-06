﻿#region License

/*
 * Copyright © 2013-2016 the original author or authors.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *      http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Proteus.AppMessageBus.Abstractions;
using Proteus.AppMessageBus.Serializable;

namespace Proteus.AppMessageBus
{
    public class DurableMessageBus : MessageBus, IStartableAsync, IStoppableAsync, ISendDurableCommands, IPublishDurableEvents, IAcknowledgeMessages
    {
        private List<Envelope<IDurableMessage>> _queuedEvents = new List<Envelope<IDurableMessage>>();
        private List<Envelope<IDurableMessage>> _queuedCommands = new List<Envelope<IDurableMessage>>();
        private RetryPolicy _activeRetryPolicy;
        private const bool UseIntermediateMessagePersistence = true;

        protected RetryPolicy DefaultEventRetryPolicy { get; private set; }
        protected RetryPolicy DefaultCommandRetryPolicy { get; private set; }
        public IMessageSerializer Serializer { get; set; }
        public MessagePersistence MessagePersister { get; set; }

        public DurableMessageBus()
            : this(new RetryPolicy(), new RetryPolicy())
        {
        }

        public DurableMessageBus(RetryPolicy defaultMessageRetryPolicy)
            : this(defaultMessageRetryPolicy, defaultMessageRetryPolicy)
        {
        }

        public DurableMessageBus(RetryPolicy defaultCommandRetryPolicy, RetryPolicy defaultEventRetryPolicy)
        {
            DefaultCommandRetryPolicy = defaultCommandRetryPolicy;
            DefaultEventRetryPolicy = defaultEventRetryPolicy;

            Serializer = new JsonNetSerializer();
            MessagePersister = new MessagePersistence();
        }

        public async Task Start()
        {
            await LoadPendingMessages();

            ClearExpiredCommands();
            await ProcessPendingCommands();

            ClearExpiredEvents();
            await ProcessPendingEvents();

            if (UseIntermediateMessagePersistence)
            {
                await SavePendingMessages();
            }
        }

        public async Task Stop()
        {
            await SavePendingMessages();
        }

        private async Task SavePendingMessages()
        {
            var queuedCommandStates = _queuedCommands.Select(cmdEnvelope => cmdEnvelope.EnvelopeState).ToList();
            var queuedEventStates = _queuedEvents.Select(evtEnvelope => evtEnvelope.EnvelopeState).ToList();

            var hasQueuedCommands = queuedCommandStates.Count > 0;
            var hasQueuedEvents = queuedEventStates.Count > 0;

            //process any commands or remove the stale data file if it exists
            if (hasQueuedCommands)
            {
                var serialized = Serializer.TrySerializeToString(queuedCommandStates);

                if (serialized.HasValue)
                {
                    await MessagePersister.SaveCommands(serialized.Value);
                }
                else
                {
                    Logger(string.Format("Unable to Serialize Commands: {0}", serialized.Exception));
                }
            }
            else
            {
                await MessagePersister.RemoveAllCommandsFromPersistence();
            }

            //process any events or remove the stale data file if it exists
            if (hasQueuedEvents)
            {
                var serialized = Serializer.TrySerializeToString(queuedEventStates);

                if (serialized.HasValue)
                {
                    await MessagePersister.SaveEvents(serialized.Value);
                }
                else
                {
                    Logger(string.Format("Unable to Serialize Events: {0}", serialized.Exception));
                }
            }
            else
            {
                await MessagePersister.RemoveAllEventsFromPersistence();
            }
        }


        private async Task LoadPendingMessages()
        {
            var hasNoQueuedCommands = _queuedCommands.Count == 0;
            var hasNoQueuedEvents = _queuedEvents.Count == 0;

            if (hasNoQueuedCommands)
            {
                if (await MessagePersister.CheckForCommands())
                {
                    var commands = await MessagePersister.LoadCommands();

                    var deserialized = Serializer.TryDeserialize<List<EnvelopeState<IDurableMessage>>>(commands);

                    if (deserialized.HasValue)
                    {
                        _queuedCommands = deserialized.Value.Select(state => state.GetEnvelope()).ToList();
                    }
                    else
                    {
                        Logger(string.Format("Unable to Deserialize Commands: {0}", deserialized.Exception));
                    }
                }
            }

            if (hasNoQueuedEvents)
            {
                if (await MessagePersister.CheckForEvents())
                {
                    var events = await MessagePersister.LoadEvents();

                    var deserialized = Serializer.TryDeserialize<List<EnvelopeState<IDurableMessage>>>(events);

                    if (deserialized.HasValue)
                    {
                        _queuedEvents = deserialized.Value.Select(state => state.GetEnvelope()).ToList();
                    }
                    else
                    {
                        Logger(string.Format("Unable to Deserialize Events: {0}", deserialized.Exception));
                    }
                }
            }
        }

        private void ClearExpiredCommands()
        {
            _queuedCommands.RemoveAll(env => !env.ShouldRetry || !env.MessageMatchesVersion(MessageVersion));
        }

        private void ClearExpiredEvents()
        {
            _queuedEvents.RemoveAll(env => !env.ShouldRetry || !env.MessageMatchesVersion(MessageVersion));
        }

        private async Task ProcessPendingCommands()
        {
            Logger("Processing Pending Commands...");

            var envelopes = _queuedCommands.Where(envelope => envelope.ShouldRetry).ToList();

            Logger(string.Format("{0} Pending Commands found.", envelopes.Count));

            foreach (var envelope in envelopes)
            {
                var subscribersResult = GetSubscribersFor(envelope.Message);

                //if there are no longer any subscribers to the message, we need to remove it from the queue
                //  so won't be around for further processing
                if (!subscribersResult.HasSubscribers)
                {
                    Logger(string.Format("No Subscribers found for Envelope Id = {0}.  Removing from Pending Commands.", envelope.Id));

                    //TODO: write failing test for this!
                    //_queuedEvents.Remove(envelope);
                    _queuedCommands.Remove(envelope);
                    continue;
                }

                Logger(string.Format("Republishing Pending Command Id = {0} from Envelope Id = {1} to Subscriber Key = {2}", envelope.Message.Id, envelope.Id, envelope.SubscriberKey));


                //there should be only one, so we can take the first...
                var subscriber = subscribersResult.Subscribers.First();

                var envelope1 = envelope;

                if (subscriber.Handler.CanBeAwaited())
                {
                    await Task.Run(() => subscriber.Handler(envelope1.Message));
                }
                else
                {
                    subscriber.Handler(envelope1.Message);
                }

                envelope.HasBeenRetried();

                if (!envelope.ShouldRetry)
                {
                    Logger(string.Format("Command in Envelope Id = {0} has invalid/expired Retry Policy.  Removing from Pending Commands.", envelope.Id));
                    _queuedCommands.Remove(envelope);
                }
            }
        }

        private async Task ProcessPendingEvents()
        {

            Logger("Processing Pending Events...");

            var envelopes = _queuedEvents.Where(envelope => envelope.ShouldRetry).ToList();

            Logger(string.Format("{0} Pending Events found.", envelopes.Count));

            foreach (var envelope in envelopes)
            {
                var subscribersResult = GetSubscribersFor(envelope.Message);

                //if there are no longer any subscribers to the message, we need to remove it from the queue
                //  so won't be around for further processing
                if (!subscribersResult.HasSubscribers)
                {
                    Logger(string.Format("No Subscribers found for Envelope Id = {0}.  Removing from Pending Events.", envelope.Id));

                    _queuedEvents.Remove(envelope);
                    continue;
                }

                Logger(string.Format("Republishing Pending Event Id = {0} from Envelope Id = {1} to Subscriber Key = {2}", envelope.Message.Id, envelope.Id, envelope.SubscriberKey));

                var subscriber = subscribersResult.Subscribers.Single(subscr=>subscr.Key== envelope.SubscriberKey);
                var envelope1 = envelope;

                if (subscriber.Handler.CanBeAwaited())
                {
                    await Task.Run(() => subscriber.Handler(envelope1.Message));
                }
                else
                {
                    subscriber.Handler(envelope1.Message);
                }


                envelope.HasBeenRetried();

                if (!envelope.ShouldRetry)
                {
                    Logger(string.Format("Event in Envelope Id = {0} has invalid/expired Retry Policy.  Removing from Pending Events.", envelope.Id));
                    _queuedEvents.Remove(envelope);
                }
            }
        }

        public async Task Acknowledge<TMessage>(TMessage message) where TMessage : IDurableMessage
        {
            Logger(string.Format("Acknowledgment received for Message of type {0} Id = {1} having Acknowledgment Id = {2}", typeof(TMessage).Name, message.Id,
                                 message.AcknowledgmentId));

            if (message is ICommand)
            {
                Logger(string.Format("Acknowledging Command of type {0} Id = {1} having Acknowledgment Id = {2}",
                                     typeof(TMessage).Name, message.Id, message.AcknowledgmentId));

                _queuedCommands.RemoveAll(env => env.Message.AcknowledgmentId == message.AcknowledgmentId);
            }

            if (message is IEvent)
            {
                Logger(string.Format("Acknowledging Event of type {0} Id = {1} having Acknowledgment Id = {2}",
                                     typeof(TMessage).Name, message.Id, message.AcknowledgmentId));

                var acknowledgmentId = message.AcknowledgmentId;
                _queuedEvents.RemoveAll(env => env.Message.AcknowledgmentId == acknowledgmentId);
            }

            if (UseIntermediateMessagePersistence)
            {
                await SavePendingMessages();
            }
        }


        protected override TCommand PrepareCommandForSending<TCommand>(TCommand command, MessageSubscriber subscriber)
        {
            Logger(string.Format("Preparing to Send Command of type {0}, MessageId = {1}", typeof(TCommand).Name, command.Id));

            command = base.PrepareCommandForSending(command, subscriber);

            var durableCommand = command as IDurableMessage;

            if (null == durableCommand)
                return command;

            durableCommand.AcknowledgmentId = Guid.NewGuid();

            StoreCommand(durableCommand);

            return (TCommand)durableCommand;
        }

        protected override TEvent PrepareEventForPublishing<TEvent>(TEvent @event, string subscriberKey, IList<MessageSubscriber> subscribers)
        {
            Logger(string.Format("Preparing to Publish Event of type {0}, MessageId = {1}, Subscriber Index = {2}", typeof(TEvent).Name, @event.Id, subscriberKey));

            @event = base.PrepareEventForPublishing(@event, subscriberKey, subscribers);

            var durableEvent = @event as IDurableMessage;

            if (null == durableEvent)
                return @event;

            durableEvent.AcknowledgmentId = Guid.NewGuid();

            var clonedEvent = Clone((TEvent)durableEvent);

            StoreEvent((IDurableMessage)clonedEvent, subscriberKey);

            return clonedEvent;
        }


        private TSource Clone<TSource>(TSource source)
        {
            var serialized = Serializer.SerializeToString(source);
            return Serializer.Deserialize<TSource>(serialized);
        }

        public async Task SendDurable<TCommand>(TCommand command) where TCommand : IDurableCommand
        {
            await SendDurable(command, DefaultCommandRetryPolicy);
        }

        public async Task SendDurable<TCommand>(TCommand command, RetryPolicy retryPolicy) where TCommand : IDurableCommand
        {
            Logger(string.Format("Sending Durable Command of type {0}, Id = {1}", typeof(TCommand).Name, command.Id));

            _activeRetryPolicy = retryPolicy;
            await base.Send(command);

            if (UseIntermediateMessagePersistence)
            {
                await SavePendingMessages();
            }
        }

        public async Task PublishDurable<TEvent>(TEvent @event) where TEvent : IDurableEvent
        {
            await PublishDurable(@event, DefaultEventRetryPolicy);
        }

        public async Task PublishDurable<TEvent>(TEvent @event, RetryPolicy retryPolicy) where TEvent : IDurableEvent
        {
            Logger(string.Format("Publishing Durable Event of type {0}, Id = {1}", typeof(TEvent).Name, @event.Id));

            _activeRetryPolicy = retryPolicy;
            await base.Publish(@event);

            if (UseIntermediateMessagePersistence)
            {
                await SavePendingMessages();
            }
        }

        private void StoreEvent(IDurableMessage @event, string subscriberKey)
        {
            var envelope = new Envelope<IDurableMessage>(@event, _activeRetryPolicy, @event.AcknowledgmentId, subscriberKey);

            if (envelope.ShouldRetry)
            {
                _queuedEvents.Add(envelope);
            }
        }

        private void StoreCommand(IDurableMessage command)
        {
            var envelope = new Envelope<IDurableMessage>(command, _activeRetryPolicy, command.AcknowledgmentId);

            if (envelope.ShouldRetry)
            {
                _queuedCommands.Add(envelope);
            }

        }
    }
}