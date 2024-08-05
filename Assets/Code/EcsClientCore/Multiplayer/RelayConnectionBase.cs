using System;
using System.Buffers;
using FlatBuffers;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public abstract class RelayConnectionBase
  {
    private const int NATIVE_ARRAY_SIZE = 16_000;

    protected ClientLogger _logger;
    
    protected NetworkDriver     _networkDriver;
    protected FlatBufferBuilder _fbb  = new FlatBufferBuilder(100); 
    protected FlatBufferBuilder _fbb2 = new FlatBufferBuilder(100);
    protected NativeArray<byte> _sendDataBuffer =
      new NativeArray<byte>(NATIVE_ARRAY_SIZE, Allocator.Persistent, NativeArrayOptions.ClearMemory);
    protected NativeArray<byte> _receiveDataBuffer =
      new NativeArray<byte>(NATIVE_ARRAY_SIZE, Allocator.Persistent, NativeArrayOptions.ClearMemory);

    public RelayConnectionBase(ClientLogger logger)
    {
      _logger = logger;
    }

    public virtual void Dispose()
    {
      _sendDataBuffer.Dispose();
      _receiveDataBuffer.Dispose();
      _networkDriver.Dispose();
    }

    protected unsafe void SendMessageData(ArraySegment<byte> msgData, NetworkConnection connection)
    {
      var beginSendResult = _networkDriver.BeginSend(connection, out var writer);
      if (beginSendResult == 0)
      {
        if (!_sendDataBuffer.IsCreated)
        {
          _sendDataBuffer = new NativeArray<byte>(NATIVE_ARRAY_SIZE, Allocator.Persistent);
        }

        if (msgData.Count >= NATIVE_ARRAY_SIZE)
        {
          Debug.Log($"Aborting send, too large");
          _networkDriver.EndSend(writer);
          return;
        }
        
        //Debug.Log($"Sending msg data of len {msgData.Count}");
        NativeArray<byte>.Copy(msgData.Array, msgData.Offset, _sendDataBuffer, 0, msgData.Count);
        writer.WriteBytes((byte*) _sendDataBuffer.GetUnsafeReadOnlyPtr(), msgData.Count);
      }
      else
      {
        Debug.LogError($"Failed Begin Send: {beginSendResult}");
      }
      _networkDriver.EndSend(writer);
    }
    
    protected unsafe void SendMessageData(byte[] msgData, NetworkConnection connection)
    {
      var beginSendResult = _networkDriver.BeginSend(connection, out var writer);
      if (beginSendResult == 0)
      {
        if (!_sendDataBuffer.IsCreated)
        {
          _sendDataBuffer = new NativeArray<byte>(NATIVE_ARRAY_SIZE, Allocator.Persistent);
        }
        
        if (msgData.Length >= NATIVE_ARRAY_SIZE)
        {
          Debug.Log($"Aborting send, too large");
          _networkDriver.EndSend(writer);
          return;
        }
          
        //Debug.Log($"Sending msg data of len {msgData.Length}");
        NativeArray<byte>.Copy(msgData, 0, _sendDataBuffer, 0, msgData.Length);
        writer.WriteBytes((byte*) _sendDataBuffer.GetUnsafeReadOnlyPtr(), msgData.Length);
      }
      else
      {
        //Debug.LogError($"Failed Begin Send: {beginSendResult}");
      }
      _networkDriver.EndSend(writer);
    }

    /// <summary>
    /// Message handler callback must finish using content data array during execution, or copy contents. It will be recycled after callback executes.
    /// </summary>
    protected unsafe void ProcessAllReceivedMessageData(NetworkConnection connection, Action<NetworkConnection, NetworkEvent.Type, byte[], int> messageHandler)
    {
      NetworkEvent.Type eventType;
      while ((eventType = _networkDriver.PopEventForConnection(connection, out var stream)) != NetworkEvent.Type.Empty)
      {
        switch (eventType)
        {
          case NetworkEvent.Type.Connect:
            messageHandler(connection, NetworkEvent.Type.Connect, null, -1);
            Debug.Log("Received connect event");
            break;
          
          case NetworkEvent.Type.Data:
            if (!_receiveDataBuffer.IsCreated)
            {
              _receiveDataBuffer = new NativeArray<byte>(4000, Allocator.Persistent);
            }
            
            //Debug.Log($"Reading data from stream");
                        
            stream.ReadBytes((byte*)_receiveDataBuffer.GetUnsafePtr(), stream.Length);
            var len = stream.GetBytesRead();
            //Debug.Log($"Reading {len} bytes from stream");

            byte[] managedMsgData = ArrayPool<byte>.Shared.Rent(len);
            int offset = managedMsgData.Length - len;

            //we back-align data since flatbuffers expects that.
            NativeArray<byte>.Copy(_receiveDataBuffer, 0, managedMsgData, offset, len);
            messageHandler(connection, NetworkEvent.Type.Data, managedMsgData, len);
            
            ArrayPool<byte>.Shared.Return(managedMsgData);
            break;
          
          case NetworkEvent.Type.Disconnect:
            messageHandler(connection, NetworkEvent.Type.Disconnect, null, -1);
            break;
        }
      }
    }
  } 
}