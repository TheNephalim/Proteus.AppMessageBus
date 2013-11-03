﻿using System;

namespace Proteus.Infrastructure.Messaging.Portable
{
    public class RetryPolicy
    {
        public int Retries { get; private set; }
        public DateTime Expiry { get; private set; }

        public RetryPolicy()
            : this(0, TimeSpan.Zero)
        {
        }

        public RetryPolicy(int retries, TimeSpan durationUntilExpiry)
        {
            Expiry = DateTime.UtcNow + durationUntilExpiry;
            Retries = retries;
        }
    }
}