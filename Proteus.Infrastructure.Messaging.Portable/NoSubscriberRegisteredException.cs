﻿using System;

namespace Proteus.Infrastructure.Messaging.Portable
{
    public class NoSubscriberRegisteredException : InvalidOperationException
    {
        public NoSubscriberRegisteredException()
        {
        }

        public NoSubscriberRegisteredException(string message)
            : base(message)
        {
        }

        public NoSubscriberRegisteredException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}