﻿using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.OSC;
using Microsoft.Extensions.Logging;

namespace AvaloniaMiaDev.Services;

/**
 * OscSendService is responsible for encoding osc messages and sending them over OSC
 */
public class OscSendService
{
    private readonly ILogger<OscSendService> _logger;
    private readonly IOscTarget _oscTarget;

    private Socket _sendSocket;
    private readonly byte[] _sendBuffer = new byte[4096];

    private CancellationTokenSource _cts;
    public event Action<int> OnMessagesDispatched = _ => { };

    public OscSendService(
        ILogger<OscSendService> logger,
        IOscTarget oscTarget
    )
    {
        _logger = logger;
        _cts = new CancellationTokenSource();

        _oscTarget = oscTarget;

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not nameof(IOscTarget.OutPort))
            {
                return;
            }

            if (_oscTarget.OutPort == default)
            {
                return;
            }

            if (_oscTarget.DestinationAddress is not null)
            {
                UpdateTarget(new IPEndPoint(IPAddress.Parse(_oscTarget.DestinationAddress), _oscTarget.OutPort));
            }
        };
    }

    private void UpdateTarget(IPEndPoint endpoint)
    {
        _cts.Cancel();
        _sendSocket?.Close();
        _oscTarget.IsConnected = false;

        _sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            _sendSocket.Connect(endpoint);
            _oscTarget.IsConnected = true;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning($"Failed to bind to sender endpoint: {endpoint}. {ex.Message}");
        }
        finally
        {
            _cts = new CancellationTokenSource();
        }
    }

    public async Task Send(OscMessage message, CancellationToken ct)
    {
        var nextByteIndex =await  message.Encode(_sendBuffer, ct);
        if (nextByteIndex > 4096)
        {
            _logger.LogError("OSC message too large to send! Skipping this batch of messages.");
            return;
        }

        await _sendSocket?.SendAsync(_sendBuffer[..nextByteIndex])!;
        OnMessagesDispatched(1);
    }

    public async Task Send(OscMessage[] messages, CancellationToken ct)
    {
        var cbt = messages.Select(m => m.Meta).ToArray();
        var index = 0;
        while (index < cbt.Length)
        {
            var length = await Task.Run(() => FtiOsc.create_osc_bundle(_sendBuffer, cbt, messages.Length, ref index), ct);
            await _sendSocket?.SendAsync(_sendBuffer[..length])!;
        }
        OnMessagesDispatched(index);
    }
}
