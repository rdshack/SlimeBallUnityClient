using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace Indigo.EcsClientCore
{
 public interface IFakeUdpEndpoint
 {
   bool AcceptConnection(IFakeUdpEndpoint sourcePoint, string connectionId);
   void ReceiveMessage(ArraySegment<byte> data,        string connectionId);
 }
 
 public struct ReceivedMsg
 {
   public ArraySegment<byte> data;
   public string             connectionId;
 }
 
 public class FakeUdpConnection
 {
   private class SendingData
   {
     public bool               received;
     public float              timePostSent;
     public float              timeInTransit;
     public float              transitTimeReq;
     public ArraySegment<byte> data;
   }
   
   private const float DELAY_SEC = 0.06f;
   
   private IFakeUdpEndpoint _endPointConnection;
   private bool             _reliable;
   
   private List<SendingData> _sendingToEndpoint   = new List<SendingData>();
   private int               randomizationCounter = 1;

   private ArrayPool2<byte> _arrayPool = new ArrayPool2<byte>();
   
   public string ConnectionId { get; private set; }
 
   public FakeUdpConnection(string connectionId, IFakeUdpEndpoint endpoint, bool reliable)
   {
     _endPointConnection = endpoint;
     _reliable = reliable;
     ConnectionId = connectionId;
   }
 
   public void Update()
   {
     for(int i = _sendingToEndpoint.Count - 1; i >= 0; i--)
     {
       var sending = _sendingToEndpoint[i];

       if (!sending.received)
       {
         sending.timeInTransit += Time.deltaTime;
         if (sending.timeInTransit >= sending.transitTimeReq)
         {
           sending.received = true;
           _endPointConnection.ReceiveMessage(sending.data, ConnectionId);
         } 
       }
       else
       {
         sending.timePostSent += Time.deltaTime;
         if (sending.timePostSent > 1)
         {
           _arrayPool.Return(sending.data.Array);
           _sendingToEndpoint.RemoveAt(i);
         }
       }
     }
   }
 
   public void Send(ArraySegment<byte> data)
   {
     byte[] copyTarget = _arrayPool.Get(data.Count);
     int targetOffset = copyTarget.Length - data.Count;
     Array.Copy(data.Array, data.Offset, copyTarget, targetOffset, data.Count);
     ArraySegment<byte> copiedData = new ArraySegment<byte>(copyTarget, targetOffset, data.Count);
     
     //Debug.LogWarning($"incoming: {data.Count}, copied: {copiedData.Count}, type: {type}");
     var sendingData = GetSendingData(copiedData);
     if (sendingData == null)
     {
       _arrayPool.Return(copyTarget);
       return;
     }
     
     _sendingToEndpoint.Insert(0, sendingData);
   }
   
   private SendingData GetSendingData(ArraySegment<byte> data)
   {
     randomizationCounter++;
     bool drop = randomizationCounter % 2 == 0;
 
     if (drop && !_reliable)
     {
       return null;
     }
 
     bool addDelay = randomizationCounter % 2 == 0;
     float reqTransitTime = (!_reliable && addDelay) ? DELAY_SEC + 0.4f : DELAY_SEC;
     return new SendingData() {transitTimeReq = reqTransitTime, data = data};
   }
 }
 
}