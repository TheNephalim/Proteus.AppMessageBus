#region License

/*
 * Copyright � 2013-2016 the original author or authors.
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

namespace Proteus.AppMessageBus.Abstractions
{
    public interface IRegisterMessageSubscriptions
    {
        void RegisterSubscriptionFor<TMessage>(Action<TMessage> handler) where TMessage : IMessage;
        void RegisterSubscriptionFor<TMessage>(string subscriberKey, Action<TMessage> handler) where TMessage : IMessage;
        bool HasSubscriptionFor<TMessage>() where TMessage : IMessage;
        bool HasSubscription(string subscriptionKey);
        void UnRegisterAllSubscriptionsFor<TMessage>() where TMessage : IMessage;
        void UnRegisterSubscription(string subscriberKey);
    }
}