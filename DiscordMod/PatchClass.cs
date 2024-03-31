using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Handlers;

namespace DiscordTurbineChat
{
    [HarmonyPatch]
    public class PatchClass
    {
        #region Settings
        const int RETRIES = 10;

        public static Settings Settings = new();
        static string settingsPath => Path.Combine(Mod.ModPath, "Settings.json");
        private FileInfo settingsInfo = new(settingsPath);

        private JsonSerializerOptions _serializeOptions = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private void SaveSettings()
        {
            string jsonString = JsonSerializer.Serialize(Settings, _serializeOptions);

            if (!settingsInfo.RetryWrite(jsonString, RETRIES))
            {
                ModManager.Log($"Failed to save settings to {settingsPath}...", ModManager.LogLevel.Warn);
                Mod.State = ModState.Error;
            }
        }

        private void LoadSettings()
        {
            if (!settingsInfo.Exists)
            {
                ModManager.Log($"Creating {settingsInfo}...");
                SaveSettings();
            }
            else
                ModManager.Log($"Loading settings from {settingsPath}...");

            if (!settingsInfo.RetryRead(out string jsonString, RETRIES))
            {
                Mod.State = ModState.Error;
                return;
            }

            try
            {
                Settings = JsonSerializer.Deserialize<Settings>(jsonString, _serializeOptions);
            }
            catch (Exception)
            {
                ModManager.Log($"Failed to deserialize Settings: {settingsPath}", ModManager.LogLevel.Warn);
                Mod.State = ModState.Error;
                return;
            }
        }
        #endregion

        #region Start/Shutdown
        public void Start()
        {
            //Need to decide on async use
            Mod.State = ModState.Loading;
            LoadSettings();

            if (Mod.State == ModState.Error)
            {
                ModManager.DisableModByPath(Mod.ModPath);
                return;
            }

            Mod.State = ModState.Running;
        }

        public void Shutdown()
        {
            //if (Mod.State == ModState.Running)
            // Shut down enabled mod...

            //If the mod is making changes that need to be saved use this and only manually edit settings when the patch is not active.
            //SaveSettings();

            if (Mod.State == ModState.Error)
                ModManager.Log($"Improper shutdown: {Mod.ModPath}", ModManager.LogLevel.Error);
        }
        #endregion

        #region Patches

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.LogBroadcastChat), new Type[] { typeof(Channel), typeof(WorldObject), typeof(string) })]
        public static bool PreLogBroadcastChat(Channel channel, WorldObject sender, string message)
        {
            switch (channel)
            {
                case Channel.Abuse:
                    if (!PropertyManager.GetBool("chat_log_abuse").Item)
                        return false;
                    break;
                case Channel.Admin:
                    if (!PropertyManager.GetBool("chat_log_admin").Item)
                        return false;
                    break;
                case Channel.AllBroadcast: // using this to sub in for a WorldBroadcast channel which isn't technically a channel
                    if (!PropertyManager.GetBool("chat_log_global").Item)
                        return false;
                    break;
                case Channel.Audit:
                    if (!PropertyManager.GetBool("chat_log_audit").Item)
                        return false;
                    break;
                case Channel.Advocate1:
                case Channel.Advocate2:
                case Channel.Advocate3:
                    if (!PropertyManager.GetBool("chat_log_advocate").Item)
                        return false;
                    break;
                case Channel.Debug:
                    if (!PropertyManager.GetBool("chat_log_debug").Item)
                        return false;
                    break;
                case Channel.Fellow:
                case Channel.FellowBroadcast:
                    if (!PropertyManager.GetBool("chat_log_fellow").Item)
                        return false;
                    break;
                case Channel.Help:
                    if (!PropertyManager.GetBool("chat_log_help").Item)
                        return false; 
                    break;
                case Channel.Olthoi:
                    if (!PropertyManager.GetBool("chat_log_olthoi").Item)
                        return false;
                    break;
                case Channel.QA1:
                case Channel.QA2:
                    if (!PropertyManager.GetBool("chat_log_qa").Item)
                        return false; 
                    break;
                case Channel.Sentinel:
                    if (!PropertyManager.GetBool("chat_log_sentinel").Item)
                        return false; 
                    break;

                case Channel.SocietyCelHanBroadcast:
                case Channel.SocietyEldWebBroadcast:
                case Channel.SocietyRadBloBroadcast:
                    if (!PropertyManager.GetBool("chat_log_society").Item)
                        return false; 
                    break;

                case Channel.AllegianceBroadcast:
                case Channel.CoVassals:
                case Channel.Monarch:
                case Channel.Patron:
                case Channel.Vassals:
                    if (!PropertyManager.GetBool("chat_log_allegiance").Item)
                        return false; 
                    break;

                case Channel.AlArqas:
                case Channel.Holtburg:
                case Channel.Lytelthorpe:
                case Channel.Nanto:
                case Channel.Rithwic:
                case Channel.Samsur:
                case Channel.Shoushi:
                case Channel.Yanshi:
                case Channel.Yaraq:
                    if (!PropertyManager.GetBool("chat_log_townchans").Item)
                        return false;
                    break;

                default:
                    return false; 
            }


            // narrow channels for webhook here
            if (channel == Channel.Audit && Settings.TurbineChatWebhookAudit.Length > 0)
                _ = WebhookRepository.SendWebhookChat(
                    DiscordChatChannel.Audit,
                    $"[CHAT][{channel.ToString().ToUpper()}] {(sender != null ? sender.Name : "[SYSTEM]")} says on the {channel} channel, \"{message}\"",
                    Settings.TurbineChatWebhookAudit
                    );

            if (channel != Channel.AllBroadcast)
                ModManager.Log($"[CHAT][{channel.ToString().ToUpper()}] {(sender != null ? sender.Name : "[SYSTEM]")} says on the {channel} channel, \"{message}\"");
            else
                ModManager.Log($"[CHAT][GLOBAL] {(sender != null ? sender.Name : "[SYSTEM]")} issued a world broadcast, \"{message}\"");


            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TurbineChatHandler), nameof(TurbineChatHandler.TurbineChatReceived), new Type[] { typeof(ClientMessage), typeof(Session) })]
        public static bool PreTurbineChatReceived(ClientMessage clientMessage, Session session)
        {
            if (!PropertyManager.GetBool("use_turbine_chat").Item)
                return false;;

            clientMessage.Payload.ReadUInt32(); // Bytes to follow
            var chatBlobType = (ChatNetworkBlobType)clientMessage.Payload.ReadUInt32();
            var chatBlobDispatchType = (ChatNetworkBlobDispatchType)clientMessage.Payload.ReadUInt32();
            clientMessage.Payload.ReadUInt32(); // Always 1
            clientMessage.Payload.ReadUInt32(); // Always 0
            clientMessage.Payload.ReadUInt32(); // Always 0
            clientMessage.Payload.ReadUInt32(); // Always 0
            clientMessage.Payload.ReadUInt32(); // Always 0
            clientMessage.Payload.ReadUInt32(); // Bytes to follow

            if (session.Player.IsGagged)
            {
                session.Player.SendGagError();
                return false;;
            }

            if (chatBlobType == ChatNetworkBlobType.NETBLOB_REQUEST_BINARY)
            {
                var contextId = clientMessage.Payload.ReadUInt32(); // 0x01 - 0x71 (maybe higher), typically though 0x01 - 0x0F
                clientMessage.Payload.ReadUInt32(); // Always 2
                clientMessage.Payload.ReadUInt32(); // Always 2
                var channelID = clientMessage.Payload.ReadUInt32();

                int messageLen = clientMessage.Payload.ReadByte();
                if ((messageLen & 0x80) > 0) // PackedByte
                {
                    byte lowbyte = clientMessage.Payload.ReadByte();
                    messageLen = ((messageLen & 0x7F) << 8) | lowbyte;
                }
                var messageBytes = clientMessage.Payload.ReadBytes(messageLen * 2);
                var message = Encoding.Unicode.GetString(messageBytes);

                clientMessage.Payload.ReadUInt32(); // Always 0x0C
                var senderID = clientMessage.Payload.ReadUInt32();
                clientMessage.Payload.ReadUInt32(); // Always 0
                var chatType = (ChatType)clientMessage.Payload.ReadUInt32();


                var adjustedChannelID = channelID;
                var adjustedchatType = chatType;

                if (chatBlobDispatchType == ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME)
                {
                    adjustedChannelID = chatType switch
                    {
                        ChatType.Allegiance => TurbineChatChannel.Allegiance,
                        ChatType.General => TurbineChatChannel.General,
                        ChatType.Trade => TurbineChatChannel.Trade,
                        ChatType.LFG => TurbineChatChannel.LFG,
                        ChatType.Roleplay => TurbineChatChannel.Roleplay,
                        ChatType.Society => TurbineChatChannel.Society,
                        ChatType.SocietyCelHan => TurbineChatChannel.Society,
                        ChatType.SocietyEldWeb => TurbineChatChannel.Society,
                        ChatType.SocietyRadBlo => TurbineChatChannel.Society,
                        ChatType.Olthoi => TurbineChatChannel.Olthoi,
                        _ => TurbineChatChannel.General
                    };

                    adjustedchatType = (ChatType)adjustedChannelID;
                }
                else if (chatBlobDispatchType == ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYID)
                {
                    if (channelID > TurbineChatChannel.Olthoi || channelID == TurbineChatChannel.Allegiance) // Channel must be an allegiance channel
                        adjustedchatType = ChatType.Allegiance;
                    else if (channelID == TurbineChatChannel.Olthoi)  // Channel must be the Olthoi play channel
                        adjustedchatType = ChatType.Olthoi;
                    else if (channelID >= TurbineChatChannel.Society) // Channel must be a society restricted channel
                        adjustedchatType = ChatType.Society;
                    else                                              // Channel must be one of the channels available to all players
                    {
                        if (channelID == TurbineChatChannel.General)
                            adjustedchatType = ChatType.General;
                        else if (channelID == TurbineChatChannel.Trade)
                            adjustedchatType = ChatType.Trade;
                        else if (channelID == TurbineChatChannel.LFG)
                            adjustedchatType = ChatType.LFG;
                        else if (channelID == TurbineChatChannel.Roleplay)
                            adjustedchatType = ChatType.Roleplay;
                    }
                }

                if (channelID != adjustedChannelID)
                    ModManager.Log($"[CHAT] ChannelID ({channelID}) was adjusted to {adjustedChannelID} | ChatNetworkBlobDispatchType: {chatBlobDispatchType}", ModManager.LogLevel.Debug);

                if (chatType != adjustedchatType)
                    ModManager.Log($"[CHAT] ChatType ({chatType}) was adjusted to {adjustedchatType} | ChatNetworkBlobDispatchType: {chatBlobDispatchType}", ModManager.LogLevel.Debug);

                var gameMessageTurbineChat = new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_EVENT_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, adjustedChannelID, session.Player.Name, message, senderID, adjustedchatType);

                if (adjustedChannelID > TurbineChatChannel.Olthoi || adjustedChannelID == TurbineChatChannel.Allegiance) // Channel must be an allegiance channel
                {
                    //var allegiance = AllegianceManager.FindAllegiance(channelID);
                    var allegiance = AllegianceManager.GetAllegiance(session.Player);
                    if (allegiance != null)
                    {
                        // is sender booted / gagged?
                        if (!allegiance.IsMember(session.Player.Guid)) return false;;
                        if (allegiance.IsFiltered(session.Player.Guid)) return false;;

                        // iterate through all allegiance members
                        foreach (var member in allegiance.Members.Keys)
                        {
                            // is this allegiance member online?
                            var online = PlayerManager.GetOnlinePlayer(member);
                            if (online == null)
                                continue;

                            // is this member booted / gagged?
                            if (allegiance.IsFiltered(member) || online.SquelchManager.Squelches.Contains(session.Player, ChatMessageType.Allegiance)) continue;

                            // does this player have allegiance chat filtered?
                            if (!online.GetCharacterOption(CharacterOption.ListenToAllegianceChat)) continue;

                            online.Session.Network.EnqueueSend(gameMessageTurbineChat);
                        }

                        session.Network.EnqueueSend(new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_RESPONSE_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, contextId, null, null, 0, adjustedchatType));
                    }
                }
                else if (adjustedChannelID == TurbineChatChannel.Olthoi) // Channel must be the Olthoi play channel
                {
                    if (!session.Player.IsOlthoiPlayer) return false;;

                    if (PropertyManager.GetBool("chat_disable_olthoi").Item)
                    {
                        TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, string.Empty);
                        return false;;
                    }

                    if (PropertyManager.GetBool("chat_echo_only").Item)
                    {
                        session.Network.EnqueueSend(gameMessageTurbineChat);
                        session.Network.EnqueueSend(new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_RESPONSE_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, contextId, null, null, 0, adjustedchatType));
                        return false;;
                    }

                    //if (PropertyManager.GetBool("chat_requires_account_15days").Item && !session.Player.Account15Days)
                    //{
                    //    TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, "because this account is not 15 days old");
                    //    return false;;
                    //}

                    //var chat_requires_account_time_seconds = PropertyManager.GetLong("chat_requires_account_time_seconds").Item;
                    //if (chat_requires_account_time_seconds > 0 && (DateTime.UtcNow - session.Player.Account.CreateTime).TotalSeconds < chat_requires_account_time_seconds)
                    //{
                    //    TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, "because this account is not old enough");
                    //    return false;;
                    //}

                    //var chat_requires_player_age = PropertyManager.GetLong("chat_requires_player_age").Item;
                    //if (chat_requires_player_age > 0 && session.Player.Age < chat_requires_player_age)
                    //{
                    //    TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, "because this character has not been played enough");
                    //    return false;;
                    //}

                    //var chat_requires_player_level = PropertyManager.GetLong("chat_requires_player_level").Item;
                    //if (chat_requires_player_level > 0 && session.Player.Level < chat_requires_player_level)
                    //{
                    //    TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, $"because this character has reached level {chat_requires_player_level}");
                    //    return false;;
                    //}

                    foreach (var recipient in PlayerManager.GetAllOnline())
                    {
                        // handle filters
                        if (!recipient.IsOlthoiPlayer && !recipient.IsAdmin)
                            continue;

                        if (PropertyManager.GetBool("chat_disable_olthoi").Item)
                        {
                            if (PropertyManager.GetBool("chat_echo_reject").Item)
                                session.Network.EnqueueSend(gameMessageTurbineChat);

                            session.Network.EnqueueSend(new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_RESPONSE_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, contextId, null, null, 0, adjustedchatType));
                            return false;;
                        }

                        if (recipient.SquelchManager.Squelches.Contains(session.Player, ChatMessageType.AllChannels))
                            continue;

                        recipient.Session.Network.EnqueueSend(gameMessageTurbineChat);
                    }

                    session.Network.EnqueueSend(new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_RESPONSE_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, contextId, null, null, 0, adjustedchatType));
                }
                else if (adjustedChannelID >= TurbineChatChannel.Society) // Channel must be a society restricted channel
                {
                    var senderSociety = session.Player.Society;

                    //var adjustedChatType = senderSociety switch
                    //{
                    //    FactionBits.CelestialHand => ChatType.SocietyCelHan,
                    //    FactionBits.EldrytchWeb => ChatType.SocietyEldWeb,
                    //    FactionBits.RadiantBlood => ChatType.SocietyRadBlo,
                    //    _ => ChatType.Society
                    //};

                    //gameMessageTurbineChat = new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_EVENT_BINARY, channelID, session.Player.Name, message, senderID, adjustedChatType);

                    if (senderSociety == FactionBits.None)
                    {
                        ChatPacket.SendServerMessage(session, "You do not belong to a society.", ChatMessageType.Broadcast); // I don't know if this is how it was done on the live servers
                        return false;;
                    }

                    foreach (var recipient in PlayerManager.GetAllOnline())
                    {
                        // handle filters
                        if (senderSociety != recipient.Society && !recipient.IsAdmin)
                            continue;

                        if (!recipient.GetCharacterOption(CharacterOption.ListenToSocietyChat))
                            continue;

                        if (recipient.SquelchManager.Squelches.Contains(session.Player, ChatMessageType.AllChannels))
                            continue;

                        recipient.Session.Network.EnqueueSend(gameMessageTurbineChat);
                    }

                    session.Network.EnqueueSend(new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_RESPONSE_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, contextId, null, null, 0, adjustedchatType));
                }
                else // Channel must be one of the channels available to all players
                {
                    if (session.Player.IsOlthoiPlayer)
                    {
                        //TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, "because this account is not 15 days old");
                        return false;;
                    }

                    if (PropertyManager.GetBool("chat_echo_only").Item)
                    {
                        session.Network.EnqueueSend(gameMessageTurbineChat);
                        session.Network.EnqueueSend(new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_RESPONSE_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, contextId, null, null, 0, adjustedchatType));
                        return false;;
                    }

                    if (PropertyManager.GetBool("chat_requires_account_15days").Item && !session.Player.Account15Days)
                    {
                        TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, "because this account is not 15 days old");
                        return false;;
                    }

                    var chat_requires_account_time_seconds = PropertyManager.GetLong("chat_requires_account_time_seconds").Item;
                    if (chat_requires_account_time_seconds > 0 && (DateTime.UtcNow - session.Player.Account.CreateTime).TotalSeconds < chat_requires_account_time_seconds)
                    {
                        TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, "because this account is not old enough");
                        return false;;
                    }

                    var chat_requires_player_age = PropertyManager.GetLong("chat_requires_player_age").Item;
                    if (chat_requires_player_age > 0 && session.Player.Age < chat_requires_player_age)
                    {
                        TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, "because this character has not been played enough");
                        return false;;
                    }

                    var chat_requires_player_level = PropertyManager.GetLong("chat_requires_player_level").Item;
                    if (chat_requires_player_level > 0 && session.Player.Level < chat_requires_player_level)
                    {
                        TurbineChatHandler.HandleChatReject(session, contextId, chatType, gameMessageTurbineChat, $"because this character has reached level {chat_requires_player_level}");
                        return false;;
                    }

                    foreach (var recipient in PlayerManager.GetAllOnline())
                    {
                        // handle filters
                        if (channelID == TurbineChatChannel.General && !recipient.GetCharacterOption(CharacterOption.ListenToGeneralChat) ||
                            channelID == TurbineChatChannel.Trade && !recipient.GetCharacterOption(CharacterOption.ListenToTradeChat) ||
                            channelID == TurbineChatChannel.LFG && !recipient.GetCharacterOption(CharacterOption.ListenToLFGChat) ||
                            channelID == TurbineChatChannel.Roleplay && !recipient.GetCharacterOption(CharacterOption.ListenToRoleplayChat))
                            continue;

                        if ((channelID == TurbineChatChannel.General && PropertyManager.GetBool("chat_disable_general").Item)
                            || (channelID == TurbineChatChannel.Trade && PropertyManager.GetBool("chat_disable_trade").Item)
                            || (channelID == TurbineChatChannel.LFG && PropertyManager.GetBool("chat_disable_lfg").Item)
                            || (channelID == TurbineChatChannel.Roleplay && PropertyManager.GetBool("chat_disable_roleplay").Item))
                        {
                            if (PropertyManager.GetBool("chat_echo_reject").Item)
                                session.Network.EnqueueSend(gameMessageTurbineChat);

                            session.Network.EnqueueSend(new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_RESPONSE_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, contextId, null, null, 0, adjustedchatType));
                            return false;;
                        }

                        if (recipient.IsOlthoiPlayer)
                            continue;

                        if (recipient.SquelchManager.Squelches.Contains(session.Player, ChatMessageType.AllChannels))
                            continue;

                        recipient.Session.Network.EnqueueSend(gameMessageTurbineChat);
                    }

                    session.Network.EnqueueSend(new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_RESPONSE_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, contextId, null, null, 0, adjustedchatType));
                }

                if (adjustedchatType == ChatType.General)
                {
                    var channel = ChatType.General;
                    var sender = session.Player;
                    var formattedMessage = $"[CHAT][{channel.ToString().ToUpper()}] {(sender != null ? sender.Name : "[SYSTEM]")} says on the {channel} channel, \"{message}\"";
                    _ = WebhookRepository.SendWebhookChat(DiscordChatChannel.General, formattedMessage, Settings.TurbineChatWebhookGeneral);
                }

                TurbineChatHandler.LogTurbineChat(adjustedChannelID, session.Player.Name, message, senderID, adjustedchatType);
            }
            else
                Console.WriteLine($"Unhandled TurbineChatHandler ChatNetworkBlobType: 0x{(uint)chatBlobType:X4}");

            return false;
        }





        #endregion
    }

}
