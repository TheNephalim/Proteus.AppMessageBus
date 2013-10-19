﻿using Proteus.Infrastructure.Messaging.Portable.Abstractions;
using Windows8TestingHarness.Messages;

namespace Windows8TestingHarness.Subscribers
{
    public class ChangeNameCommandHandler : IHandle<ChangeNameCommand>
    {
        public void Handle(ChangeNameCommand message)
        {
            //TODO: actually change the backing data in persistence (or other work!)

            //let anyone who cares know that this has happened
            App.Bus.Publish(new NameChangedEvent(message.NewFirstname, message.NewLastname));
        }
    }
}