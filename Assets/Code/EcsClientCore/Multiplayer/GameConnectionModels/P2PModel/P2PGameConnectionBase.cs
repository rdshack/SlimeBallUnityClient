using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;
using Unity.Networking.Transport;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public interface IP2PGameHostConnection : IP2PGameConnection
  {
    void         SetHostReady();
    Task<string> HostRelayServer(string regionId);
  }

  public abstract class P2PGameConnectionBase : RelayConnectionBase
  {
    private List<int>               _tempFrameNumList   = new List<int>();
    private List<long>              _tempTimestampsList = new List<long>();
    private List<Offset<ByteArray>> _byteArrayOffets    = new List<Offset<ByteArray>>();

    protected P2PGameConnectionBase(ClientLogger logger) : base(logger)
    {
    }
    
    public void SendPeerSyncData(List<SerializedPlayerFrameInputData> frameInputs, 
                                         int                          lastInputAckFromPeer, 
                                         int                          firstFrame, 
                                         List<int>                    hashes, 
                                         int                          hashStart, 
                                         int                          lastHashAckFromPeer)
    {
      _fbb.Clear();
      _byteArrayOffets.Clear();
      _tempFrameNumList.Clear();
      _tempTimestampsList.Clear();
      
      foreach (var frameInput in frameInputs)
      {
        _tempFrameNumList.Add(frameInput.frameNum);
        _tempTimestampsList.Add(frameInput.locallyAppliedTimestamp);
        VectorOffset byteVectorOffset = FlatBufferUtil.AddVectorToBufferFromByteArray(_fbb, frameInput.serializedData, frameInput.serializedDataLength);
        _byteArrayOffets.Add(Messages.ByteArray.CreateByteArray(_fbb, byteVectorOffset));
      }
      
      
      VectorOffset inputsOffset = FlatBufferUtil.AddVectorToBufferFromOffsetList(_fbb, Messages.PlayerInputContent.StartInputsVector, _byteArrayOffets);
      

      VectorOffset timestampsOffset = FlatBufferUtil.AddVectorToBufferFromLongList(_fbb, Messages.PlayerInputContent.StartFrameCreationTimestampsVector,
                                                                                   _tempTimestampsList);
      
      VectorOffset hashesOffset = FlatBufferUtil.AddVectorToBufferFromIntList(_fbb, Messages.PlayerInputContent.StartFrameCreationTimestampsVector,
                                                                              hashes);
      
      var content = Messages.P2PPlayerSyncContent.CreateP2PPlayerSyncContent(_fbb, 
                                                                             lastInputAckFromPeer, 
                                                                             firstFrame, 
                                                                             inputsOffset, 
                                                                             timestampsOffset,
                                                                             lastHashAckFromPeer,
                                                                             hashStart,
                                                                             hashesOffset
                                                                            );
      _fbb.Finish(content.Value);

      int len = _fbb.DataBuffer.Length - _fbb.DataBuffer.Position;
      var contentData = _fbb.DataBuffer.ToArraySegment(_fbb.DataBuffer.Position, len);
      
      _fbb2.Clear();
      VectorOffset contentVectorOffset = FlatBufferUtil.AddVectorToBufferFromByteArraySeg(_fbb2, contentData);

      var finalMsg = ConnectionUtil.CreateGameMsg(_fbb2, GameMessageType.P2PSyncContent, contentVectorOffset, out string guid);
      _fbb2.Finish(finalMsg.Value);

      var finalMsgData =
        _fbb2.DataBuffer.ToArraySegment(_fbb2.DataBuffer.Position, _fbb2.DataBuffer.Length - _fbb2.DataBuffer.Position);
      SendMessageData(finalMsgData, GetPeerConnection());
      
      _logger.Log(ClientLogger.ClientLogFlags.Sync, $"{guid}: Sending peer sync data: input start is {firstFrame} of length {frameInputs.Count}. " +
                                                                $"Hash start is {hashStart} of length {hashes.Count} " +
                                                                $"and latestInputAck is {lastInputAckFromPeer} " +
                                                                $"and latestHashAck is {lastHashAckFromPeer}");
    }
    
    public abstract int               GetPlayerSlot();
    public abstract NetworkConnection GetPeerConnection();
    
  }
}