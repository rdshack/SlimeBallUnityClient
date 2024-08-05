using System;
using System.Collections;
using System.Collections.Generic;
using FlatBuffers;
using Messages;
using UnityEngine;

public static class ConnectionUtil
{
    public static Offset<Messages.GameMessage> CreateGameMsg(FlatBufferBuilder fbb, GameMessageType msgType, VectorOffset content)
    {
        var guid = fbb.CreateString(Guid.NewGuid().ToString());
        long time = DateTime.UtcNow.ToFileTimeUtc();
        
        return Messages.GameMessage.CreateGameMessage(fbb, guid, time, msgType, content);
    }
    
    public static Offset<Messages.GameMessage> CreateGameMsg(FlatBufferBuilder fbb, GameMessageType msgType, VectorOffset content, out string guid)
    {
        guid = Guid.NewGuid().ToString();
        var guidOffset = fbb.CreateString(guid);
        long time = DateTime.UtcNow.ToFileTimeUtc();
        
        return Messages.GameMessage.CreateGameMessage(fbb, guidOffset, time, msgType, content);
    }
}
