﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Ircx.Objects;
using CSharpTools;
using System.Reflection;

namespace Core.Ircx.Commands
{
    class ACCESS : Command
    {

        public ACCESS(CommandCode Code) : base(Code)
        {
            base.MinParamCount = 1;
            base.RegistrationRequired = true;
            base.DataType = CommandDataType.Data;
        }

        public new COM_RESULT Execute(Frame Frame)
        {
            Server server = Frame.Server;
            User user = Frame.User;
            Message message = Frame.Message;

            if (message.Data.Count >= 1)
            {
                Access AccessObject = null;
                UserAccessLevel requiredLevel = UserAccessLevel.NoAccess;
                String8 AccessObjectName = new String8(message.Data[0].bytes, 0, message.Data[0].length);
                AccessObjectName.toupper();

                Obj obj = server.GetObject(AccessObjectName);

                UserAccessLevel UserLevel = user.Level;

                if (obj != null)
                {
                    switch (obj.ObjectType)
                    {
                        case ObjType.ChannelObject:
                            {
                                Channel c = server.Channels.GetChannel(AccessObjectName);
                                if (c != null)
                                {

                                    UserChannelInfo uci = user.GetChannelInfo(c);

                                    if (Flood.FloodCheck(base.DataType, uci) == FLD_RESULT.S_WAIT) { return COM_RESULT.COM_WAIT; }

                                    if (UserLevel < UserAccessLevel.ChatGuide)
                                    {
                                        if (uci != null)
                                        {
                                            UserLevel = uci.Member.Level;
                                        }
                                    }

                                    AccessObject = c.Access;
                                    if (UserLevel < UserAccessLevel.ChatSysop)
                                    {
                                        if ((uci != null) && (UserLevel >= UserAccessLevel.ChatHost)) { requiredLevel = UserAccessLevel.ChatHost; }
                                        else { requiredLevel = UserAccessLevel.NoAccess; }
                                    }
                                    else
                                    {
                                        requiredLevel = UserAccessLevel.ChatSysop;
                                    }
                                }
                                break;
                            }
                        case ObjType.UserObject:
                            {
                                if (obj == user)
                                {
                                    AccessObject = user.Access;
                                    requiredLevel = UserAccessLevel.None;
                                }
                                break;
                            }
                        case ObjType.ServerObject:
                            {
                                AccessObject = server.Access;
                                requiredLevel = UserAccessLevel.ChatSysop;
                                break;
                            }
                    }
                }

                if (AccessObject != null)
                {
                    if (UserLevel >= requiredLevel)
                    {
                        //proceed with attempting to do the request
                        if (message.Data.Count == 1)
                        {
                            ProcessList(server, AccessObject, user, message);
                            //Invoke List
                        }
                        else if (message.Data.Count >= 2)
                        {
                            EnumAccessOperator Operator = AccessObject.ResolveAccessOperator(message.Data[1]);
                            if (Operator != EnumAccessOperator.NONE)
                            {
                                if (Operator == EnumAccessOperator.LIST)
                                {
                                    //Go ahead and list if possible and end here
                                    ProcessList(server, AccessObject, user, message);
                                    return COM_RESULT.COM_SUCCESS;
                                }

                                AccessLevel Level;
                                if (message.Data.Count >= 3) { Level = AccessObject.ResolveAccessLevel(message.Data[2]); }
                                else { Level = AccessLevel.None; }

                                if (Operator == EnumAccessOperator.CLEAR)
                                {
                                    ProcessClear(server, AccessObject, user, UserLevel, message, Level);
                                }
                                else if (Operator == EnumAccessOperator.ADD)
                                {
                                    if (Level != AccessLevel.None)
                                    {
                                        //attempt to process request
                                        ProcessAdd(server, AccessObject, user, UserLevel, message, Level);
                                    }
                                    else
                                    {
                                        //<- :Default-Chat-Community 461 Sky ACCESS :Not enough parameters
                                        user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { message.Command }));
                                    }
                                }
                                else if (Operator == EnumAccessOperator.DELETE)
                                {
                                    if (Level != AccessLevel.None)
                                    {
                                        ProcessDelete(server, AccessObject, user, UserLevel, message, Level);
                                        //attempt to process request
                                    }
                                    else
                                    {
                                        //<- :Default-Chat-Community 461 Sky ACCESS :Not enough parameters
                                        user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { message.Command }));
                                    }
                                }
                            }
                            else
                            {
                                //<- :Default-Chat-Community 900 Sky moo :Bad command
                                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_BADCOMMAND_900, Data: new String8[] { message.Data[1] }));
                            }


                        }
                    }
                    else
                    {
                        //<- :Default-Chat-Community 913 Sky2k #x :No access
                        user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NOACCESS_913, Data: new String8[] { message.Data[0] }));
                    }
                }
                else
                {
                    //<- :Default-Chat-Community 924 Sky test :No such object found
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NOSUCHOBJECT_924, Data: new String8[] { message.Data[0] }));
                }

            }
            else
            {
                //insufficient parameters
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { message.Command }));
            }
            //
            return COM_RESULT.COM_SUCCESS;
        }

        public void ProcessList(Server server, Access Access, User user, Message message)
        {
            if (message.Data.Count <= 3)
            {
                //by this point the user will already be at least host...
                /*
                 <- :Default-Chat-Community 803 Sky #x :Start of access entries
                 <- :Default-Chat-Community 804 Sky #x OWNER *!*@*$* 0 ~no@127.0.0.1 :
                 <- :Default-Chat-Community 805 Sky #x :End of access entries
                 */
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_RPL_ACCESSSTART_803, Data: new String8[] { Access.ObjectName }));

                for (int i = 0; i < Access.Entries.Entries.Count; i++)
                {
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_RPL_ACCESSLIST_804, Data: new String8[] { Access.ObjectName,
                                                                                                             Access.Entries.Entries[i].Level.LevelText,
                                                                                                             Access.Entries.Entries[i].Mask._address[3],
                                                                                                             Access.Entries.Entries[i].EntryAddress,
                                                                                                             Access.Entries.Entries[i].Reason },
                                                                                             IData: new int[] { Access.Entries.Entries[i].DurationInSeconds }));
                }

                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_RPL_ACCESSEND_805, Data: new String8[] { Access.ObjectName }));
            }
            else
            {
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_TOOMANYARGUMENTS_901, Data: new String8[] { message.Data[3] }));
                //<- :Default-Chat-Community 901 Sky e :Too many arguments
            }
        }

        public bool TryClear(Access Access, User user, UserAccessLevel UserAccessLevel, AccessLevel level)
        {
            bool bException = false;
            for (int i = Access.Entries.Entries.Count - 1; i >= 0; i--)
            {
                if ((level.Level == EnumAccessLevel.All) || (level.Level == Access.Entries.Entries[i].Level.Level))
                {
                    if (Access.Entries.Entries[i].EntryLevel <= UserAccessLevel)
                    {
                        Access.Entries.Entries.RemoveAt(i);
                    }
                    else { bException = true; }
                }
            }
            return bException;
        }

        public void ProcessClear(Server server, Access Access, User user, UserAccessLevel UserAccessLevel, Message message, AccessLevel level)
        {
            if (level.Level == EnumAccessLevel.NONE) { level.Level = EnumAccessLevel.All; }

            bool bException = TryClear(Access, user, UserAccessLevel, level);
            if (!bException)
            {
                //success
                //<- :Default-Chat-Community 820 Sky #x * :Clear
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_RPL_ACCESSCLEAR_820, Data: new String8[] { Access.ObjectName, (level.Level == EnumAccessLevel.All ? Resources.Wildcard : level.LevelText) }));
            }
            else
            {
                //<- :Default-Chat-Community 922 Sky2k :Some entries not cleared due to security
                //some have not been removed...
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_ACCESSNOTCLEAR_922));
            }
        }
        public void ProcessAdd(Server server, Access Access, User user, UserAccessLevel UserAccessLevel, Message message, AccessLevel level)
        {
            if ((UserAccessLevel == UserAccessLevel.ChatHost) && (level.Level == EnumAccessLevel.OWNER))
            {
                //<- :Default-Chat-Community 903 Sky2k OWNER :Bad level
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_BADLEVEL_903, Data: new String8[] { message.Data[2] }));
                return;
            }

            if (message.Data.Count >= 4)
            {

                Address Mask = new Address();
                if (Mask.FromMask(message.Data[3]))
                {
                    if (Access.Entries.Contains(Mask) == null)
                    {

                        if (Access.Entries.Entries.Count >= 250)
                        {
                            user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_TOOMANYACCESSES_916));
                            //:Default-Chat-Community 914 Sky :Duplicate access entry
                            return;
                        }

                        int duration = 0;
                        if (message.Data.Count >= 5) { duration = CSharpTools.Tools.Str2Int(message.Data[4]); }

                        if ((duration == -1) || (duration > 999999))
                        {
                            user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_BADCOMMAND_900, Data: new String8[] { message.Data[4] }));
                            //<- :organisa-e679d0 900 Sky a :Bad command
                            return;
                        }

                        String8 Reason = Resources.Null;
                        if (message.Data.Count >= 6) { Reason = message.Data[5]; }
                        //Would be good to check the length of the reason with a given limit... Exchange 5.5 flat out does not care

                        AccessEntry ae = new AccessEntry();

                        if (duration == 0)
                        {
                            ae.Fixed = true;
                        }
                        else
                        {
                            duration *= 60; //Turn duration in to seconds...
                        }

                        ae.Level = level;
                        ae.Mask = Mask;
                        ae.EntryLevel = UserAccessLevel;
                        ae.EntryAddress = user.Address._address[1]; //userhost@hostname
                        ae.Duration = duration;
                        ae.Reason = Reason;

                        Access.Entries.Add(ae);
                        //<- :Default-Chat-Community 801 Sky #x OWNER B!*@*$* 0 ~no@127.0.0.1 :
                        user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_RPL_ACCESSADD_801, Data: new String8[] { Access.ObjectName, ae.Level.LevelText, ae.Mask._address[3], ae.EntryAddress, ae.Reason }, IData: new int[] { ae.DurationInSeconds }));
                    }
                    else
                    {
                        //<- :Default-Chat-Community 914 Sky :Duplicate access entry
                        user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_DUPACCESS_914));
                    }
                }
                else
                {
                    //<- :Default-Chat-Community 461 Sky ACCESS :Not enough parameters
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { message.Data[0] }));

                }
            }
            else
            {
                //<- :organisa-e679d0 903 Sky #Test :Bad level
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_BADLEVEL_903, Data: new String8[] { message.Data[1] }));
            }

        }
        public void ProcessDelete(Server server, Access Access, User user, UserAccessLevel UserAccessLevel, Message message, AccessLevel level)
        {
            if ((UserAccessLevel == UserAccessLevel.ChatHost) && (level.Level == EnumAccessLevel.OWNER))
            {
                //<- :Default-Chat-Community 903 Sky2k OWNER :Bad level
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_BADLEVEL_903, Data: new String8[] { message.Data[3] }));
                return;
            }

            if (message.Data.Count >= 3)
            {

                Address Mask = new Address();
                if (Mask.FromMask(message.Data[3]))
                {
                    AccessEntry ae = Access.Entries.Contains(Mask);
                    if (ae != null)
                    {
                        if (ae.EntryLevel <= UserAccessLevel)
                        {
                            Access.Entries.Remove(ae);
                            //<- :Default-Chat-Community 801 Sky #x OWNER B!*@*$* 0 ~no@127.0.0.1 :
                            user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_RPL_ACCESSDELETE_802, Data: new String8[] { Access.ObjectName, ae.Level.LevelText, ae.Mask._address[3], ae.EntryAddress, ae.Reason }, IData: new int[] { ae.DurationInSeconds }));
                        }
                    }
                    else
                    {
                        //<- :Default-Chat-Community 915 Sky :Unknown access entry
                        user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_MISACCESS_915));
                    }
                }
                else
                {
                    //<- :Default-Chat-Community 461 Sky ACCESS :Not enough parameters
                    user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_NEEDMOREPARAMS_461, Data: new String8[] { message.Command }));

                }
            }
            else
            {
                //<- :organisa-e679d0 903 Sky #Test :Bad level
                user.Send(Raws.Create(Server: server, Client: user, Raw: Raws.IRCX_ERR_BADLEVEL_903, Data: new String8[] { message.Data[0] }));
            }
        }
    }
}
