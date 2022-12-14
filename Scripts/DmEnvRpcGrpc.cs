// <auto-generated>
//     Generated by the protocol buffer compiler.  DO NOT EDIT!
//     source: dm_env_rpc/v1/dm_env_rpc.proto
// </auto-generated>
// Original file comments:
// Copyright 2019 DeepMind Technologies Limited. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or  implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ===========================================================================
#pragma warning disable 0414, 1591
#region Designer generated code

using grpc = global::Grpc.Core;

namespace DmEnvRpc.V1 {
  public static partial class Environment
  {
    static readonly string __ServiceName = "dm_env_rpc.v1.Environment";

    [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
    static void __Helper_SerializeMessage(global::Google.Protobuf.IMessage message, grpc::SerializationContext context)
    {
      #if !GRPC_DISABLE_PROTOBUF_BUFFER_SERIALIZATION
      if (message is global::Google.Protobuf.IBufferMessage)
      {
        context.SetPayloadLength(message.CalculateSize());
        global::Google.Protobuf.MessageExtensions.WriteTo(message, context.GetBufferWriter());
        context.Complete();
        return;
      }
      #endif
      context.Complete(global::Google.Protobuf.MessageExtensions.ToByteArray(message));
    }

    [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
    static class __Helper_MessageCache<T>
    {
      public static readonly bool IsBufferMessage = global::System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(global::Google.Protobuf.IBufferMessage)).IsAssignableFrom(typeof(T));
    }

    [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
    static T __Helper_DeserializeMessage<T>(grpc::DeserializationContext context, global::Google.Protobuf.MessageParser<T> parser) where T : global::Google.Protobuf.IMessage<T>
    {
      #if !GRPC_DISABLE_PROTOBUF_BUFFER_SERIALIZATION
      if (__Helper_MessageCache<T>.IsBufferMessage)
      {
        return parser.ParseFrom(context.PayloadAsReadOnlySequence());
      }
      #endif
      return parser.ParseFrom(context.PayloadAsNewBuffer());
    }

    [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
    static readonly grpc::Marshaller<global::DmEnvRpc.V1.EnvironmentRequest> __Marshaller_dm_env_rpc_v1_EnvironmentRequest = grpc::Marshallers.Create(__Helper_SerializeMessage, context => __Helper_DeserializeMessage(context, global::DmEnvRpc.V1.EnvironmentRequest.Parser));
    [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
    static readonly grpc::Marshaller<global::DmEnvRpc.V1.EnvironmentResponse> __Marshaller_dm_env_rpc_v1_EnvironmentResponse = grpc::Marshallers.Create(__Helper_SerializeMessage, context => __Helper_DeserializeMessage(context, global::DmEnvRpc.V1.EnvironmentResponse.Parser));

    [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
    static readonly grpc::Method<global::DmEnvRpc.V1.EnvironmentRequest, global::DmEnvRpc.V1.EnvironmentResponse> __Method_Process = new grpc::Method<global::DmEnvRpc.V1.EnvironmentRequest, global::DmEnvRpc.V1.EnvironmentResponse>(
        grpc::MethodType.DuplexStreaming,
        __ServiceName,
        "Process",
        __Marshaller_dm_env_rpc_v1_EnvironmentRequest,
        __Marshaller_dm_env_rpc_v1_EnvironmentResponse);

    /// <summary>Service descriptor</summary>
    public static global::Google.Protobuf.Reflection.ServiceDescriptor Descriptor
    {
      get { return global::DmEnvRpc.V1.DmEnvRpcReflection.Descriptor.Services[0]; }
    }

    /// <summary>Base class for server-side implementations of Environment</summary>
    [grpc::BindServiceMethod(typeof(Environment), "BindService")]
    public abstract partial class EnvironmentBase
    {
      /// <summary>
      /// Process incoming environment requests.
      /// </summary>
      /// <param name="requestStream">Used for reading requests from the client.</param>
      /// <param name="responseStream">Used for sending responses back to the client.</param>
      /// <param name="context">The context of the server-side call handler being invoked.</param>
      /// <returns>A task indicating completion of the handler.</returns>
      [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
      public virtual global::System.Threading.Tasks.Task Process(grpc::IAsyncStreamReader<global::DmEnvRpc.V1.EnvironmentRequest> requestStream, grpc::IServerStreamWriter<global::DmEnvRpc.V1.EnvironmentResponse> responseStream, grpc::ServerCallContext context)
      {
        throw new grpc::RpcException(new grpc::Status(grpc::StatusCode.Unimplemented, ""));
      }

    }

    /// <summary>Client for Environment</summary>
    public partial class EnvironmentClient : grpc::ClientBase<EnvironmentClient>
    {
      /// <summary>Creates a new client for Environment</summary>
      /// <param name="channel">The channel to use to make remote calls.</param>
      [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
      public EnvironmentClient(grpc::ChannelBase channel) : base(channel)
      {
      }
      /// <summary>Creates a new client for Environment that uses a custom <c>CallInvoker</c>.</summary>
      /// <param name="callInvoker">The callInvoker to use to make remote calls.</param>
      [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
      public EnvironmentClient(grpc::CallInvoker callInvoker) : base(callInvoker)
      {
      }
      /// <summary>Protected parameterless constructor to allow creation of test doubles.</summary>
      [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
      protected EnvironmentClient() : base()
      {
      }
      /// <summary>Protected constructor to allow creation of configured clients.</summary>
      /// <param name="configuration">The client configuration.</param>
      [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
      protected EnvironmentClient(ClientBaseConfiguration configuration) : base(configuration)
      {
      }

      /// <summary>
      /// Process incoming environment requests.
      /// </summary>
      /// <param name="headers">The initial metadata to send with the call. This parameter is optional.</param>
      /// <param name="deadline">An optional deadline for the call. The call will be cancelled if deadline is hit.</param>
      /// <param name="cancellationToken">An optional token for canceling the call.</param>
      /// <returns>The call object.</returns>
      [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
      public virtual grpc::AsyncDuplexStreamingCall<global::DmEnvRpc.V1.EnvironmentRequest, global::DmEnvRpc.V1.EnvironmentResponse> Process(grpc::Metadata headers = null, global::System.DateTime? deadline = null, global::System.Threading.CancellationToken cancellationToken = default(global::System.Threading.CancellationToken))
      {
        return Process(new grpc::CallOptions(headers, deadline, cancellationToken));
      }
      /// <summary>
      /// Process incoming environment requests.
      /// </summary>
      /// <param name="options">The options for the call.</param>
      /// <returns>The call object.</returns>
      [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
      public virtual grpc::AsyncDuplexStreamingCall<global::DmEnvRpc.V1.EnvironmentRequest, global::DmEnvRpc.V1.EnvironmentResponse> Process(grpc::CallOptions options)
      {
        return CallInvoker.AsyncDuplexStreamingCall(__Method_Process, null, options);
      }
      /// <summary>Creates a new instance of client from given <c>ClientBaseConfiguration</c>.</summary>
      [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
      protected override EnvironmentClient NewInstance(ClientBaseConfiguration configuration)
      {
        return new EnvironmentClient(configuration);
      }
    }

    /// <summary>Creates service definition that can be registered with a server</summary>
    /// <param name="serviceImpl">An object implementing the server-side handling logic.</param>
    [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
    public static grpc::ServerServiceDefinition BindService(EnvironmentBase serviceImpl)
    {
      return grpc::ServerServiceDefinition.CreateBuilder()
          .AddMethod(__Method_Process, serviceImpl.Process).Build();
    }

    /// <summary>Register service method with a service binder with or without implementation. Useful when customizing the service binding logic.
    /// Note: this method is part of an experimental API that can change or be removed without any prior notice.</summary>
    /// <param name="serviceBinder">Service methods will be bound by calling <c>AddMethod</c> on this object.</param>
    /// <param name="serviceImpl">An object implementing the server-side handling logic.</param>
    [global::System.CodeDom.Compiler.GeneratedCode("grpc_csharp_plugin", null)]
    public static void BindService(grpc::ServiceBinderBase serviceBinder, EnvironmentBase serviceImpl)
    {
      serviceBinder.AddMethod(__Method_Process, serviceImpl == null ? null : new grpc::DuplexStreamingServerMethod<global::DmEnvRpc.V1.EnvironmentRequest, global::DmEnvRpc.V1.EnvironmentResponse>(serviceImpl.Process));
    }

  }
}
#endregion
