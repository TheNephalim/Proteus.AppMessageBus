﻿using System;

namespace Proteus.AppMessageBus.Portable
{
    public class DuplicateSubscriberRegisteredException : InvalidOperationException
    {
        public DuplicateSubscriberRegisteredException()
        {
        }

        public DuplicateSubscriberRegisteredException(string message)
            : base(message)
        {
        }

        public DuplicateSubscriberRegisteredException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}