//
// DhtMessageFactory.cs
//
// Authors:
//   Alan McGovern <alan.mcgovern@gmail.com>
//
// Copyright (C) 2008 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;

using MonoTorrent.BEncoding;

namespace MonoTorrent.Dht.Messages
{
    class DhtMessageFactory
    {
        static readonly BEncodedString QueryNameKey = new BEncodedString ("q");
        static readonly BEncodedString MessageTypeKey = new BEncodedString ("y");
        static readonly BEncodedString TransactionIdKey = new BEncodedString ("t");
        static readonly Dictionary<BEncodedString, Func<BEncodedDictionary, DhtMessage>> queryDecoders = new Dictionary<BEncodedString, Func<BEncodedDictionary, DhtMessage>> ();

        readonly Dictionary<(BEncodedValue, IPEndPoint), QueryMessage> messages = new Dictionary<(BEncodedValue, IPEndPoint), QueryMessage> ();


        public int RegisteredMessages => messages.Count;

        static DhtMessageFactory ()
        {
            queryDecoders.Add (new BEncodedString ("announce_peer"), d => new AnnouncePeer (d));
            queryDecoders.Add (new BEncodedString ("find_node"), d => new FindNode (d));
            queryDecoders.Add (new BEncodedString ("get_peers"), d => new GetPeers (d));
            queryDecoders.Add (new BEncodedString ("ping"), d => new Ping (d));
        }

        internal bool IsRegistered (BEncodedValue transactionId, IPEndPoint destination)
        {
            return messages.ContainsKey ((transactionId, destination));
        }

        public void RegisterSend (QueryMessage message, IPEndPoint destination)
        {
            if (message is null)
                throw new ArgumentNullException (nameof (message));
            if (message.TransactionId is null)
                throw new ArgumentException ("The message must have a transaction id set");
            if (destination is null)
                throw new ArgumentNullException (nameof (destination));

            messages.Add (((BEncodedValue) message.TransactionId, destination), message);
        }

        public bool UnregisterSend (QueryMessage message, IPEndPoint destination)
        {
            if (message is null)
                throw new ArgumentNullException (nameof (message));
            if (message.TransactionId is null)
                throw new ArgumentException ("The message must have a transaction id set");
            if (destination is null)
                throw new ArgumentNullException (nameof (destination));

            return messages.Remove ((message.TransactionId, destination));
        }

        public DhtMessage DecodeMessage (BEncodedDictionary dictionary, IPEndPoint source)
        {
            if (!TryDecodeMessage (dictionary, source, out DhtMessage? message, out string? error))
                throw new MessageException (ErrorCode.GenericError, error!);

            return message;
        }

        public bool TryDecodeMessage (BEncodedDictionary dictionary, IPEndPoint source, [NotNullWhen (true)] out DhtMessage? message)
        {
            return TryDecodeMessage (dictionary, source, out message, out string? error);
        }

        public bool TryDecodeMessage (BEncodedDictionary dictionary, IPEndPoint? source, [NotNullWhen(true)] out DhtMessage? message, out string? error)
        {
            message = null;
            error = null;

            if (!dictionary.TryGetValue (MessageTypeKey, out BEncodedValue? messageType)) {
                message = null;
                error = "The BEncodedDictionary did not contain the 'q' key, so the message type could not be identified";
                return false;
            }

            if (messageType.Equals (QueryMessage.QueryType)) {
                message = queryDecoders[(BEncodedString) dictionary[QueryNameKey]] (dictionary);
            } else if (messageType.Equals (ErrorMessage.ErrorType)) {
                message = new ErrorMessage (dictionary);
                messages.Remove ((message.TransactionId!, source!));
            } else {
                if (source is null)
                    throw new InvalidOperationException ("Attempted to decode a response but no source IP was supplied");
                var key = ((BEncodedValue) dictionary[TransactionIdKey], source!);
                if (messages.TryGetValue (key, out QueryMessage? query)) {
                    messages.Remove (key);
                    try {
                        message = query.CreateResponse (dictionary);
                    } catch {
                        error = "Response dictionary was invalid";
                    }
                } else {
                    error = "Response had bad transaction ID";
                }
            }

            // If the transaction ID is null, or invalid, we should bail out
            if (message != null && message.TransactionId == null)
                error = "Response had a null transation ID";

            // If the node ID is null, or invalid, we should bail out
            if (message != null && message.Id == null)
                error = "Response had a null node ID";

            return error == null && message != null;
        }
    }
}
