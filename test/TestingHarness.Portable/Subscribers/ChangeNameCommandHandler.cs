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

using Proteus.AppMessageBus.Portable.Abstractions;
using TestingHarness.Portable.Messages;

namespace TestingHarness.Portable.Subscribers
{
    public class ChangeNameCommandHandler : IHandle<ChangeNameCommand>
    {
        private readonly IPublishEvents _bus;

        public ChangeNameCommandHandler(IPublishEvents bus)
        {
            _bus = bus;
        }

        public void Handle(ChangeNameCommand message)
        {
            if (message.IsValidToHandle)
            {
                //once its actually completed, let anyone who cares know that this has happened
                _bus.Publish(new NameChangedEvent(message.NewFirstname, message.NewLastname));    
            }
        }
    }
}