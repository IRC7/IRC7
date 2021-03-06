﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Ircx.Objects;
using CSharpTools;

namespace Core.Ircx.Commands
{
    class MODE : Command
    {
        public MODE(CommandCode Code) : base(Code)
        {
            base.MinParamCount = 1;
            base.DataType = CommandDataType.Data;
        }

        public static COM_RESULT ProcessChannelModes(Server server, User user, Channel channel, Message message)
        {
            //Mode %#Lobby <modes> <param1> <param2> ...
            if (message.Data.Count == 1)
            {
                user.Send(Raws.Create(Server: server, Channel: channel, Client: user, Raw: Raws.IRCX_RPL_MODE_324, Data: new String8[] { channel.Modes.ChannelModeString }));
                //Display Modes to user
            }
            else if (message.Data.Count > 1)
            {
                UserChannelInfo uci = user.GetChannelInfo(channel);

                if (uci == null)
                {
                    // you're not on that channel
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NOTONCHANNEL_442, Data: new String8[] { message.Data[0] }));
                }
                else if (uci.Member.ChannelMode.UserMode < ChanUserMode.Host)
                {
                    //you are not an operator
                    user.Send(Raws.Create(Server: server, Channel: channel, Client: user, Raw: Raws.IRCX_ERR_CHANOPRIVSNEEDED_482));
                }
                else
                {
                    /*
                     * Notes: All errors are sent to user before mode data is sent to channel
                     *        All positive modes, including user mode changes are reported to channel
                     *        before negative ones, therefore two Lists will need to be created here
                     */

                    if (Flood.FloodCheck(CommandDataType.Data, uci) == FLD_RESULT.S_WAIT) { return COM_RESULT.COM_WAIT; }

                    AuditModeReport Report = new AuditModeReport();
                    bool bModeFlag = true;
                    message.ParamOffset = 2;
                    for (int i = 0; i < message.Data[1].length; i++)
                    {
                        switch (message.Data[1].bytes[i])
                        {
                            case (byte)'-': { bModeFlag = false; break; }
                            case (byte)'+': { bModeFlag = true; break; }
                            default:
                                {
                                    Mode m = channel.Modes.ResolveMode(message.Data[1].bytes[i]);
                                    if (m != null)
                                    {
                                        //At this point its Host vs Owner vs Oper

                                        //if m.Function is null then its a usermode and needs processing via the chanuser mode function
                                        //else ...

                                        if (uci.Member.ChannelMode.UserMode >= m.Level)
                                        {
                                            if (m.Function != null)
                                            {
                                                if (!Report.GetModeFlagProcessed(m.ModeChar))
                                                {
                                                    if (((ChannelModeFunction)m.Function).Execute(server, channel, user, bModeFlag, ref Report, message))
                                                    {
                                                        Report.Modes.Add(new AuditMode(m.ModeChar, bModeFlag));
                                                        Report.SetModeFlagProcessed(m.ModeChar);
                                                    }
                                                    else
                                                    {
                                                        //if report contains no modes and we are at the end of the string then update modes
                                                        //update wont be invoked unless the mode change was announced
                                                        if ((Report.Modes.Count == 0) && (i + 1 == message.Data[1].length))
                                                        {
                                                            channel.Modes.UpdateModes(channel.Properties.Memberkey);
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                //user mode
                                                ProcessChanUserMode(server, uci.Member, channel, message, Report, m, bModeFlag);
                                            }

                                        }
                                        else
                                        {
                                            //no permissions
                                            user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_SECURITY_908));
                                        }
                                    }
                                    else
                                    {
                                        // unknown mode char
                                        user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_UNKNOWNMODE_472, IData: new int[] { message.Data[1].bytes[i] }));
                                    }
                                    break;
                                }
                        }
                    }
                    ProcessChanModeReport(server, user, channel, Report);
                    ProcessChanUserModeReport(server, user, channel, Report);
                }
            }
            return COM_RESULT.COM_SUCCESS;
        }

        public static void ProcessChanModeReport(Server server, User user, Channel channel, AuditModeReport Report)
        {
            String8 addModes = new String8(Report.Modes.Count + 1);
            String8 delModes = new String8(Report.Modes.Count + 1);

            for (int i = 0; i < Report.Modes.Count; i++)
            {
                switch (Report.Modes[i].modeFlag)
                {
                    case true:
                        {
                            if (addModes.length == 0) { addModes.append('+'); }
                            addModes.append(Report.Modes[i].modeChar); break;
                        }
                    default:
                        {
                            if (delModes.length == 0) { delModes.append('-'); }
                            delModes.append(Report.Modes[i].modeChar); break;
                        }
                }
            }

            if ((addModes.length > 0) || (delModes.length > 0))
            {
                channel.Modes.UpdateModes(channel.Properties.Memberkey);
                channel.Send(Raws.Create(Server: server, Channel: channel, Client: user, Raw: Raws.RPL_MODE_IRC, Data: new String8[] { channel.Name, addModes, delModes }), user);
            }

        }
        public static void ProcessUserReport(Server server, User user, User TargetUser, AuditModeReport Report)
        {
            String8 addModes = new String8(Report.UserModes.Count + 1);
            String8 delModes = new String8(Report.UserModes.Count + 1);

            for (int i = 0; i < Report.UserModes.Count; i++)
            {
                switch (Report.UserModes[i].modeFlag)
                {
                    case true:
                        {
                            if (addModes.length == 0) { addModes.append('+'); }
                            addModes.append(Report.UserModes[i].modeChar); break;
                        }
                    default:
                        {
                            if (delModes.length == 0) { delModes.append('-'); }
                            delModes.append(Report.UserModes[i].modeChar); break;
                        }
                }
            }

            if ((addModes.length > 0) || (delModes.length > 0))
            {
                TargetUser.Modes.UpdateModes();
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.RPL_MODE_IRC, Data: new String8[] { TargetUser.Address.Nickname, addModes, delModes }));
            }
        }
        public static void ProcessChanUserModeReport(Server server, User user, Channel channel, AuditModeReport Report)
        {
            if (Report.UserModes.Count > 0)
            {
                for (int i = 0; i < Report.UserModes.Count; i++)
                {
                    User TargetUser = (Report.UserModes[i].TargetUser == null ? user : Report.UserModes[i].TargetUser);
                    channel.Send(Raws.Create(Server: server, Channel: channel, Client: user, Raw: Raws.RPL_MODE_IRC, Data: new String8[] { channel.Name, Report.UserModes[i].modeData, Report.UserModes[i].user }), TargetUser);
                }
            }
        }


        public static void ProcessChanUserMode(Server server, ChannelMember Member, Channel channel, Message message, AuditModeReport Report, Mode m, bool bModeFlag)
        {
            String8 UserParam = message.GetNextParam();
            if (UserParam != null)
            {
                ChannelMember TargetUser = channel.Members.GetMemberByName(UserParam);
                if (TargetUser != null)
                {
                    if ((TargetUser.Level > Member.Level) && (TargetUser.Level >= UserAccessLevel.ChatGuide))
                    {
                        //You are not an irc-operator
                        Member.User.Send(Raws.Create(Server: server, Client: Member.User, Raw: Raws.IRCX_ERR_NOPRIVILEGES_481));
                    }
                    else if (TargetUser.Level <= Member.Level)
                    {
                        //Can perform operation
                        switch (m.ModeChar)
                        {
                            case (byte)'q':
                                {
                                    if ((Member.ChannelMode.IsOwner()) || (Member.ChannelMode.IsAdmin()))
                                    {
                                        if (bModeFlag)
                                        {
                                            //-o
                                            if (TargetUser.ChannelMode.IsHost())
                                            {
                                                TargetUser.ChannelMode.SetHost(false);
                                                Report.UserModes.Add(new AuditUserMode(TargetUser.User, TargetUser.User.Address.Nickname, (byte)Resources.ChannelUserModeCharHost, false));
                                            }
                                            //+q
                                            TargetUser.ChannelMode.SetOwner(bModeFlag);
                                            Report.UserModes.Add(new AuditUserMode(TargetUser.User, TargetUser.User.Address.Nickname, m.ModeChar, bModeFlag));
                                        }
                                        else
                                        {
                                            //-q
                                            TargetUser.ChannelMode.SetOwner(bModeFlag);
                                            Report.UserModes.Add(new AuditUserMode(TargetUser.User, TargetUser.User.Address.Nickname, m.ModeChar, bModeFlag));
                                            //-o
                                            if (TargetUser.ChannelMode.IsHost())
                                            {
                                                TargetUser.ChannelMode.SetHost(false);
                                                Report.UserModes.Add(new AuditUserMode(TargetUser.User, TargetUser.User.Address.Nickname, (byte)Resources.ChannelUserModeCharHost, false));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Member.User.Send(Raws.Create(Server: server, Channel: channel, Client: Member.User, Raw: Raws.IRCX_ERR_CHANQPRIVSNEEDED_485));
                                        //You are not an owner.
                                    }
                                    break;
                                }
                            case (byte)'o':
                                {
                                    if ((TargetUser.ChannelMode.IsOwner()) || (TargetUser.ChannelMode.IsAdmin()))
                                    {
                                        TargetUser.ChannelMode.SetOwner(false);
                                        Report.UserModes.Add(new AuditUserMode(TargetUser.User, TargetUser.User.Address.Nickname, (byte)Resources.ChannelUserModeCharOwner, false));
                                    }

                                    TargetUser.ChannelMode.SetHost(bModeFlag);
                                    Report.UserModes.Add(new AuditUserMode(TargetUser.User, TargetUser.User.Address.Nickname, m.ModeChar, bModeFlag));
                                    break;
                                }
                            case (byte)'v': { TargetUser.ChannelMode.SetVoice(bModeFlag); Report.UserModes.Add(new AuditUserMode(TargetUser.User, TargetUser.User.Address.Nickname, m.ModeChar, bModeFlag)); break; }
                        }
                    }
                    else
                    {
                        //You are not channel owner (only probable outcome at this stage)
                        Member.User.Send(Raws.Create(Server: server, Channel: channel, Client: Member.User, Raw: Raws.IRCX_ERR_CHANQPRIVSNEEDED_485));
                    }
                }
                else
                {
                    Member.User.Send(Raws.Create(Server: server, Client: Member.User, Raw: Raws.IRCX_ERR_NOSUCHNICK_401, Data: new String8[] { UserParam }));
                    //no such nick/channel
                }
            }
            else
            {
                //insufficient paramters
                Member.User.Send(Raws.Create(Server: server, Client: Member.User, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { Resources.CommandMode, AuditUserMode.CreateModeData(bModeFlag, m.ModeChar, true) }));
            }



        }

        public void ProcessUserModes(Server server, User user, Message message)
        {
            //Is it for the current user?
            //Resolve other user
            //User does not exist?
            //-> SERVER MODE s +x
            //<- :SERVER 403 sky9 s :No such channel
            //User does exist?
            //Is Oper/Admin?
            //Else <- :SERVER 403 sky9 s :No such channel

            String8 Object = message.Data[0];
            User TargetUser = null;

            if (!String8.compareCaseInsensitive(Object, user.Name))
            {
                TargetUser = user;
            }
            else { TargetUser = server.Users.GetUser(Object); }

            if (TargetUser == user) ; //Self Mode
            else if (user.Level < UserAccessLevel.ChatGuide) //If user and not a self mode
            {
                //Else <- :SERVER 403 sky9 s :No such channel
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NOSUCHCHANNEL_403, Data: new String8[] { message.Data[0] }));
                return;
            }
            else if (user.Level >= UserAccessLevel.ChatGuide)
            {
                if (TargetUser == null) //If there really is no such user then 403
                {
                    //Else <- :SERVER 403 sky9 s :No such channel
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NOSUCHCHANNEL_403, Data: new String8[] { message.Data[0] }));
                    return;
                }
            }

            //At this point its either a self mode or an admin change so go ahead

            if (message.Data.Count == 1)
            {
                user.Send(Raws.Create(Server: server, Client: TargetUser, Raw: Raws.IRCX_RPL_UMODEIS_221, Data: new String8[] { TargetUser.Modes.UserModeString }));
            }
            else if (message.Data.Count > 1)
            {
                AuditModeReport Report = new AuditModeReport();
                bool bModeFlag = true;
                message.ParamOffset = 2;
                for (int i = 0; i < message.Data[1].length; i++)
                {
                    switch (message.Data[1].bytes[i])
                    {
                        case (byte)'-': { bModeFlag = false; break; }
                        case (byte)'+': { bModeFlag = true; break; }
                        default:
                            {
                                Mode m = TargetUser.Modes.ResolveMode(message.Data[1].bytes[i]);
                                if (m != null)
                                {
                                    if (!Report.GetModeFlagProcessed(m.ModeChar))
                                    {
                                        if (m.Function != null) { 
                                            if (((UserModeFunction)m.Function).Execute(server, user, TargetUser, bModeFlag, ref Report, message))
                                            {
                                                Report.UserModes.Add(new AuditUserMode(TargetUser, TargetUser.Address.Nickname, m.ModeChar, bModeFlag));
                                                Report.SetModeFlagProcessed(m.ModeChar);
                                            }
                                        }
                                        else
                                        {
                                            //cannot be set
                                            user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_SECURITY_908));
                                        }
                                    }
                                }
                                else
                                {
                                    // unknown mode char
                                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_UNKNOWNMODE_472, IData: new int[] { message.Data[1].bytes[i] }));
                                }
                                break;
                            }
                    }
                }

                if (Report.UserModes.Count > 0) { ProcessUserReport(server, user, TargetUser, Report); }
            }
        }

        public new COM_RESULT Execute(Frame Frame)
        {
            Server server = Frame.Server;
            User user = Frame.User;
            Message message = Frame.Message;
            if (message.Data.Count >= 1)
            {
                if (user.Registered)
                {
                    String8 ObjectName = message.Data[0];
                    Channel c = null;
                    if (Channel.IsChannel(ObjectName))
                    {
                        c = server.Channels.GetChannel(ObjectName);
                        if (c != null)
                        {
                            // Process Channel Modes
                            return ProcessChannelModes(server, user, c, message);
                        }
                        else
                        {
                            user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NOSUCHCHANNEL_403, Data: new String8[] { message.Data[0] }));
                            return COM_RESULT.COM_SUCCESS;
                        }
                    }
                    else if (Client.IsObject(ObjectName))
                    {
                        c = server.Channels.GetChannel(ObjectName);
                        if (c != null)
                        {
                            message.Data[0] = c.Name;
                            // Process Channel Modes
                            return ProcessChannelModes(server, user, c, message);
                        }
                    }
                    if (Flood.FloodCheck(base.DataType, Frame.User) == FLD_RESULT.S_WAIT) { return COM_RESULT.COM_WAIT; }
                    ProcessUserModes(server, user, message);
                }
                else
                {
                    if (message.Data[0] == Resources.ISIRCX)
                    {
                        IRCX.ProcessIRCXReply(Frame);
                    }
                    else
                    {
                        /* have not registered */
                        user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NOTREGISTERED_451));
                    }

                }
            }
            else
            {
                if (user.Registered)
                {
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { message.Command }));
                }
                else
                {
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NOTREGISTERED_451));
                }
                /* insufficient params */
            }

            return COM_RESULT.COM_SUCCESS;
            //
        }
        ////
    }

    #region User Mode Functions
    public class UserModeAdminFunction : UserModeFunction
    {
        public override bool Execute(Server server, User user, User TargetUser, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            if ((user == TargetUser) && (user.Level == UserAccessLevel.ChatAdministrator)) // Only an admin can do this to himself
            {
                TargetUser.Modes.Admin.Value = (bModeFlag == true ? 1 : 0); return true;
            }
            else if (user.Level == UserAccessLevel.ChatAdministrator)
            {
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_USERSDONTMATCH_502));
            }
            else { user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_SECURITY_908)); }
            return false;
        }
    }
    public class UserModeOperFunction : UserModeFunction
    {
        //:SERVER 502 Anonymous :Cant change mode for other users
        public override bool Execute(Server server, User user, User TargetUser, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            if ((user == TargetUser) && (user.Level >= UserAccessLevel.ChatGuide)) // Only an admin can do this or oper to himself
            {
                TargetUser.Modes.Oper.Value = (bModeFlag == true ? 1 : 0); return true;
            }
            else if (user.Level >= UserAccessLevel.ChatGuide)
            {
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_USERSDONTMATCH_502));
            }
            else { user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_SECURITY_908)); }
            return false;
        }
    }
    public class UserModeInvisibleFunction : UserModeFunction
    {
        public static void ToggleInvisible(Server server, User user, int iFlag)
        {
            if (user.Modes.Invisible.Value == 1)
            {
                server.InvisibleStatus(user, false);
                user.Modes.Invisible.Value = 0;
            }
            else
            {
                server.InvisibleStatus(user, true);
                user.Modes.Invisible.Value = 1;
            }
        }

        public override bool Execute(Server server, User user, User TargetUser, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            ToggleInvisible(server, user, (bModeFlag == true ? 1 : 0));
            return true;
        }
    }
    public class UserModeIrcxFunction : UserModeFunction
    {
        public override bool Execute(Server server, User user, User TargetUser, bool bModeFlag, ref AuditModeReport report, Message message)
        { TargetUser.Modes.Ircx.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class UserModeGagFunction : UserModeFunction
    {
        public override bool Execute(Server server, User user, User TargetUser, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            if ((user.Level > TargetUser.Level) && (user.Level >= UserAccessLevel.ChatGuide)) // Only an admin can do this or oper to himself
            {
                if (user != TargetUser)
                {
                    TargetUser.Modes.Gag.Value = (bModeFlag == true ? 1 : 0); return true;
                }
                else
                {
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_USERSDONTMATCH_502)); //actually sent when its the same user, this is how Exchange 5.5 worked
                }
            }
            else { user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_SECURITY_908)); }
            return false;
        }
    }
    public class UserModePasskeyFunction : UserModeFunction
    {
        public override bool Execute(Server server, User user, User TargetUser, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            //    user.Modes.Gag.Value = (bModeFlag == true ? 1 : 0); return true; 
            //self mode
            if (message.Data.Count >= 3)
            {
                if (message.Data[2].Length == 0) { return false; }

                if (user.ActiveChannel != null)
                {
                    AuditModeReport ChannelReport = new AuditModeReport();
                    if (message.Data[2] == user.ActiveChannel.Channel.Properties.Ownerkey.Value)
                    {
                        //-o
                        if (user.ActiveChannel.Member.ChannelMode.IsHost())
                        {
                            TargetUser.ActiveChannel.Member.ChannelMode.SetHost(false);
                            ChannelReport.UserModes.Add(new AuditUserMode(TargetUser, TargetUser.Address.Nickname, (byte)Resources.ChannelUserModeCharHost, false));
                        }
                        //+q
                        TargetUser.ActiveChannel.Member.ChannelMode.SetOwner(true);
                        ChannelReport.UserModes.Add(new AuditUserMode(TargetUser, TargetUser.Address.Nickname, Resources.ChannelUserModeCharOwner, true));
                        MODE.ProcessChanUserModeReport(server, user, user.ActiveChannel.Channel, ChannelReport);
                    }
                    else if (message.Data[2] == user.ActiveChannel.Channel.Properties.Hostkey.Value)
                    {
                        if (TargetUser.ActiveChannel.Member.ChannelMode.IsOwner())
                        {
                            TargetUser.ActiveChannel.Member.ChannelMode.SetOwner(false);
                            ChannelReport.UserModes.Add(new AuditUserMode(TargetUser, TargetUser.Address.Nickname, (byte)Resources.ChannelUserModeCharOwner, false));
                        }

                        TargetUser.ActiveChannel.Member.ChannelMode.SetHost(true);
                        ChannelReport.UserModes.Add(new AuditUserMode(TargetUser, TargetUser.Address.Nickname, Resources.ChannelUserModeCharHost, true));
                        MODE.ProcessChanUserModeReport(server, user, user.ActiveChannel.Channel, ChannelReport);
                    }
                }
                else
                {

                    //You are not on a channel
                }
            }
            else
            {
                //not enough params
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { message.Command }));
            }
            return false;
        }
    }
    #endregion

    #region Channel Mode Functions
    public class ChannelModeAuthOnlyFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            channel.Modes.AuthOnly.Value = (bModeFlag == true ? 1 : 0);
            return true;
        }
    }
    public class ChannelModeProfanityFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            channel.Modes.Profanity.Value = (bModeFlag == true ? 1 : 0);
            return true;
        }
    }
    public class ChannelModeOnStageFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            channel.Modes.OnStage.Value = (bModeFlag == true ? 1 : 0);
            return true;
        }
    }
    public class ChannelModeHiddenFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            int modeFlag = (bModeFlag == true ? 1 : 0);
            if (modeFlag != channel.Modes.Hidden.Value)
            {
                if (modeFlag == 1)
                {
                    if (channel.Modes.Secret.Value == 1) { channel.Modes.Secret.Value = 0; report.Modes.Add(new AuditMode(Resources.ChannelModeCharSecret, false)); }
                    if (channel.Modes.Private.Value == 1) { channel.Modes.Private.Value = 0; report.Modes.Add(new AuditMode(Resources.ChannelModeCharPrivate, false)); }
                }
                report.SetModeFlagProcessed(Resources.ChannelModeCharSecret);
                report.SetModeFlagProcessed(Resources.ChannelModeCharPrivate);

                channel.Modes.Hidden.Value = modeFlag;
                return true;
            }
            return false;
        }
    }
    public class ChannelModeKeyFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            String8 KeyParam = message.GetNextParam();
            if (KeyParam != null)
            {
                if (bModeFlag)
                {
                    if (channel.Modes.Key.Value == 0)
                    {
                        channel.Modes.Key.Value = 1;
                        channel.Properties.Memberkey.Value = KeyParam;

                        report.UserModes.Add(new AuditUserMode(null, KeyParam, Resources.ChannelModeCharKey, bModeFlag));
                    }
                    else
                    {
                        //key already set
                        user.Send(Raws.Create(Server: server, Channel: channel, Client: user, Raw: Raws.IRCX_ERR_KEYSET_467));
                    }
                }
                else
                {
                    String8 UKey = new String8(channel.Properties.Memberkey.Value.bytes, 0, channel.Properties.Memberkey.Value.length);
                    String8 UKeyParam = new String8(KeyParam.bytes, 0, KeyParam.length);
                    UKeyParam.toupper();
                    UKey.toupper();

                    if (UKey == UKeyParam)
                    {
                        //unset key
                        channel.Properties.Memberkey.Value = Resources.Null;
                        channel.Modes.Key.Value = 0;
                        report.UserModes.Add(new AuditUserMode(null, KeyParam, Resources.ChannelModeCharKey, bModeFlag));
                    }
                    //there is no action if it fails
                }
            }
            else
            {
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { Resources.CommandMode, AuditUserMode.CreateModeData(bModeFlag, Resources.ChannelModeCharKey, true) }));
                //need more params
            }
            return false;
        }
    }
    public class ChannelModeInviteFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.Invite.Value = (bModeFlag == true ? 1 : 0); if (bModeFlag == false) { channel.InviteList.Clear(); }; return true; }
    }
    public class ChannelModeUserLimitFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            if (bModeFlag == false)
            {
                if (user.Level <= UserAccessLevel.ChatAdministrator)
                {
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_SECURITY_908));
                }
                else { channel.Modes.UserLimit.Value = 0; }
            }
            else
            {
                String8 LimitParam = message.GetNextParam();
                if (LimitParam != null)
                {

                    int limit = CSharpTools.Tools.Str2Int(LimitParam);
                    if (limit < 1) { limit = 100; LimitParam = Resources.DefaultUserLimit; }

                    int MaxLimit = (user.Level < UserAccessLevel.ChatGuide ? 100 : Int32.MaxValue);

                    if ((limit > 0) && (limit <= MaxLimit)) { channel.Modes.UserLimit.Value = limit; report.UserModes.Add(new AuditUserMode(null, LimitParam, Resources.ChannelModeCharUserLimit, bModeFlag)); }
                    else { user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { Resources.CommandMode, AuditUserMode.CreateModeData(bModeFlag, Resources.ChannelModeCharUserLimit, true) })); }
                }
                else
                {
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { Resources.CommandMode, AuditUserMode.CreateModeData(bModeFlag, Resources.ChannelModeCharUserLimit, true) }));
                    //need more params
                }
            }
            return false;
        }
    }
    public class ChannelModeModeratedFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.Moderated.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModeNoExternFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.NoExtern.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModePrivateFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            int modeFlag = (bModeFlag == true ? 1 : 0);
            if (modeFlag != channel.Modes.Private.Value)
            {
                if (modeFlag == 1)
                {
                    if (channel.Modes.Secret.Value == 1) { channel.Modes.Secret.Value = 0; report.Modes.Add(new AuditMode(Resources.ChannelModeCharSecret, false)); }
                    if (channel.Modes.Hidden.Value == 1) { channel.Modes.Hidden.Value = 0; report.Modes.Add(new AuditMode(Resources.ChannelModeCharHidden, false)); }
                }
                report.SetModeFlagProcessed(Resources.ChannelModeCharSecret);
                report.SetModeFlagProcessed(Resources.ChannelModeCharHidden);

                channel.Modes.Private.Value = modeFlag;
                return true;
            }
            return false;
        }
    }
    public class ChannelModeRegisteredFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.Registered.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModeSecretFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            int modeFlag = (bModeFlag == true ? 1 : 0);
            if (modeFlag != channel.Modes.Secret.Value)
            {
                if (modeFlag == 1)
                {
                    if (channel.Modes.Private.Value == 1) { channel.Modes.Private.Value = 0; report.Modes.Add(new AuditMode(Resources.ChannelModeCharPrivate, false)); }
                    if (channel.Modes.Hidden.Value == 1) { channel.Modes.Hidden.Value = 0; report.Modes.Add(new AuditMode(Resources.ChannelModeCharHidden, false)); }
                }

                report.SetModeFlagProcessed(Resources.ChannelModeCharPrivate);
                report.SetModeFlagProcessed(Resources.ChannelModeCharHidden);

                channel.Modes.Secret.Value = modeFlag;
                return true;
            }
            return false;
        }
    }
    public class ChannelModeSubscriberFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.Subscriber.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModeTopicOpFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.TopicOp.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModeKnockFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.Knock.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModeNoWhisperFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.NoWhisper.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModeNoGuestWhisperFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.NoGuestWhisper.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModeAuditoriumFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        { channel.Modes.Auditorium.Value = (bModeFlag == true ? 1 : 0); return true; }
    }
    public class ChannelModeBanFunction : ChannelModeFunction
    {
        public override bool Execute(Server server, Channel channel, User user, bool bModeFlag, ref AuditModeReport report, Message message)
        {
            //list ban access list
            //<- :Default-Chat-Community 367 Sky2k #test BarneyTheDinosaur!*@*$*
            //<- :Default-Chat-Community 368 Sky2k #test :End of Channel Ban List
            if ((bModeFlag) && (message.Data.Count == 2))
            {
                for (int i = 0; i < channel.Access.Entries.Entries.Count; i++)
                {
                    if (channel.Access.Entries.Entries[i].Level.Level == EnumAccessLevel.DENY)
                    {
                        user.Send(Raws.Create(Server: server, Channel: channel, Client: user, Raw: Raws.IRCX_RPL_BANLIST_367, Data: new String8[] { channel.Access.Entries.Entries[i].Mask._address[3] }));
                    }
                }
                user.Send(Raws.Create(Server: server, Channel: channel, Client: user, Raw: Raws.IRCX_RPL_ENDOFBANLIST_368));
            }
            return false;
        }
    }
    #endregion
    //////////
}
