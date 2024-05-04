﻿using KerbalSimpit.Core.Constants;
using KerbalSimpit.Core.Enums;
using KerbalSimpit.Core.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KerbalSimpit.Core.Peers
{
    public abstract partial class SimpitPeer
    {
        private Simpit _simpit;
        private SimpitMessageSerializer _serializer;

        private readonly ConcurrentQueue<ISimpitMessage> _read;
        private readonly ConcurrentQueue<ISimpitMessage> _write;
        private readonly SimpitStream _inbound;
        private readonly SimpitStream _outbound;
        private readonly HashSet<SimpitMessageId> _subscribedOutgoingMessageIds;


        protected ConcurrentQueue<ISimpitMessage> read => _read;
        protected ISimpitLogger logger => _simpit.Logger;
        protected CancellationToken cancellationToken => _simpit.CancellationToken;

        public abstract bool Running { get; }

        public ConnectionStatusEnum Status { get; private set; }

        public SimpitPeer()
        {
            _read = new ConcurrentQueue<ISimpitMessage>();
            _write = new ConcurrentQueue<ISimpitMessage>();
            _subscribedOutgoingMessageIds = new HashSet<SimpitMessageId>();

            _inbound = new SimpitStream();
            _outbound = new SimpitStream();
        }

        public void Dispose()
        {
            if (this.Running)
            {
                this.Stop();
            }
        }

        internal void Start(Simpit simpit)
        {
            _simpit = simpit;
            _serializer = new SimpitMessageSerializer(this.logger);

            this.Start();
        }

        internal void Stop(Simpit simpit)
        {
            _subscribedOutgoingMessageIds.Clear();

            this.Stop();
        }

        protected abstract void Start();

        protected abstract void Stop();

        /// <summary>
        /// Enqueue an outgoing message to be sent within the outbound thread.
        /// </summary>
        /// <param name="message"></param>
        public void EnqueueOutbound(ISimpitMessage message)
        {
            _write.Enqueue(message);
        }

        public bool TryRead(out ISimpitMessage message)
        {
            return _read.TryDequeue(out message);
        }

        public void Clear()
        {
            _inbound.Clear();
            _outbound.Clear();

            while (_read.TryDequeue(out _))
            {
                //
            }
        }

        public void InboundDataRecieved(byte data)
        {
            _inbound.Write(data);

            if (data != SpecialBytes.EndOfMessage)
            { // More data to be read before we've recieved a complete message
                return;
            }

            if (_serializer.TryDeserialize(_inbound, out ISimpitMessage message) == false)
            {
                this.logger.LogWarning("{0}::{1} - Unable to deserialize incoming message.", nameof(SimpitPeer), nameof(InboundDataRecieved));
                return;
            }

            this.logger.LogVerbose("{0}::{1} - Recieved message {2}", nameof(SimpitPeer), nameof(InboundDataRecieved), message.GetType().Name);
            this.read.Enqueue(message);
        }

        protected bool GetOutbound(out SimpitStream outbound)
        {
            outbound = _outbound;

            if (_write.TryDequeue(out ISimpitMessage message))
            {
                return _serializer.TrySerialize(outbound, message);
            }

            return false;
        }
    }
}
