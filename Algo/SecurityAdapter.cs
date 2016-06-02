﻿namespace StockSharp.Algo
{
	using System.Collections.Generic;
	using System.Linq;

	using Ecng.Collections;
	using Ecng.Common;

	using StockSharp.Messages;

	/// <summary>
	/// Security message adapter.
	/// </summary>
	public class SecurityAdapter : MessageAdapterWrapper
	{
		private readonly Dictionary<SecurityId, SecurityId> _securityIds = new Dictionary<SecurityId, SecurityId>();
		private readonly Dictionary<SecurityId, List<Message>> _suspendedSecurityMessages = new Dictionary<SecurityId, List<Message>>();
		private readonly SyncObject _syncRoot = new SyncObject();

		/// <summary>
		/// Initializes a new instance of the <see cref="SecurityAdapter"/>.
		/// </summary>
		/// <param name="innerAdapter">The adapter, to which messages will be directed.</param>
		public SecurityAdapter(IMessageAdapter innerAdapter)
			: base(innerAdapter)
		{
		}

		/// <summary>
		/// Process <see cref="MessageAdapterWrapper.InnerAdapter"/> output message.
		/// </summary>
		/// <param name="message">The message.</param>
		protected override void OnInnerAdapterNewOutMessage(Message message)
		{
			switch (message.Type)
			{
				case MessageTypes.Security:
				{
					var secMsg = (SecurityMessage)message;
					var securityId = secMsg.SecurityId;

					var nativeSecurityId = securityId.Native;
					var securityCode = securityId.SecurityCode;
					var boardCode = securityId.BoardCode;

					var isSecurityIdEmpty = securityCode.IsEmpty() || boardCode.IsEmpty();
					var isNativeIdNull = nativeSecurityId == null;

					if (!isNativeIdNull && !isSecurityIdEmpty)
						_securityIds[securityId] = securityId;

					base.OnInnerAdapterNewOutMessage(message);

					if (securityId.Native != null)
						ProcessSuspendedSecurityMessages(securityId);

					//необходимо обработать отложенные сообщения не только по NativeId, но и по обычным идентификаторам S#
					var stocksharpId = securityId.Native == null
							? securityId
							: new SecurityId { SecurityCode = securityId.SecurityCode, BoardCode = securityId.BoardCode };

					ProcessSuspendedSecurityMessages(stocksharpId);

					break;
				}

				case MessageTypes.Position:
				{
					var positionMsg = (PositionMessage)message;
					ProcessMessage(positionMsg.SecurityId, positionMsg);
					break;
				}

				case MessageTypes.PositionChange:
				{
					var positionMsg = (PositionChangeMessage)message;
					ProcessMessage(positionMsg.SecurityId, positionMsg);
					break;
				}

				case MessageTypes.Execution:
				{
					var execMsg = (ExecutionMessage)message;
					ProcessMessage(execMsg.SecurityId, execMsg);
					break;
				}

				case MessageTypes.Level1Change:
				{
					var level1Msg = (Level1ChangeMessage)message;
					ProcessMessage(level1Msg.SecurityId, level1Msg);
					break;
				}

				case MessageTypes.QuoteChange:
				{
					var quoteChangeMsg = (QuoteChangeMessage)message;
					ProcessMessage(quoteChangeMsg.SecurityId, quoteChangeMsg);
					break;
				}

				default:
					base.OnInnerAdapterNewOutMessage(message);
					break;
			}
		}

		/// <summary>
		/// Create a copy of <see cref="SecurityAdapter"/>.
		/// </summary>
		/// <returns>Copy.</returns>
		public override IMessageChannel Clone()
		{
			return new SecurityAdapter(InnerAdapter);
		}

		private void ProcessMessage(SecurityId securityId, Message message)
		{
			if (securityId.Native != null)
			{
				SecurityId fullSecurityId;

				if (_securityIds.TryGetValue(securityId, out fullSecurityId))
				{
					ReplaceSecurityId(message, fullSecurityId);
					base.OnInnerAdapterNewOutMessage(message);
				}
				else
				{
					lock (_syncRoot)
						_suspendedSecurityMessages.SafeAdd(securityId).Add(message.Clone());
				}
			}
			else
				base.OnInnerAdapterNewOutMessage(message);
		}

		private void ProcessSuspendedSecurityMessages(SecurityId securityId)
		{
			List<Message> msgs;

			lock (_syncRoot)
			{
				msgs = _suspendedSecurityMessages.TryGetValue(securityId);

				if (msgs != null)
					_suspendedSecurityMessages.Remove(securityId);

				// find association by code and code + type
				var pair = _suspendedSecurityMessages
					.FirstOrDefault(p =>
						p.Key.SecurityCode.CompareIgnoreCase(securityId.SecurityCode) &&
						p.Key.BoardCode.IsEmpty() &&
						(securityId.SecurityType == null || p.Key.SecurityType == securityId.SecurityType));

				if (pair.Value != null)
					_suspendedSecurityMessages.Remove(pair.Key);

				if (msgs != null)
				{
					if (pair.Value != null)
						msgs.AddRange(pair.Value);
				}
				else
					msgs = pair.Value;

				if (msgs == null)
					return;
			}

			foreach (var msg in msgs)
			{
				ReplaceSecurityId(msg, securityId);
				base.OnInnerAdapterNewOutMessage(msg);
			}
		}

		private static void ReplaceSecurityId(Message message, SecurityId securityId)
		{
			switch (message.Type)
			{
				case MessageTypes.Position:
				{
					var positionMsg = (PositionMessage)message;
					positionMsg.SecurityId = securityId;
					break;
				}

				case MessageTypes.PositionChange:
				{
					var positionMsg = (PositionChangeMessage)message;
					positionMsg.SecurityId = securityId;
					break;
				}

				case MessageTypes.Execution:
				{
					var execMsg = (ExecutionMessage)message;
					execMsg.SecurityId = securityId;
					break;
				}

				case MessageTypes.Level1Change:
				{
					var level1Msg = (Level1ChangeMessage)message;
					level1Msg.SecurityId = securityId;
					break;
				}

				case MessageTypes.QuoteChange:
				{
					var quoteChangeMsg = (QuoteChangeMessage)message;
					quoteChangeMsg.SecurityId = securityId;
					break;
				}
			}
		}
	}
}
