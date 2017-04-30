﻿using System;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using ISimpleHttpServer.Service;
using ISSDP.UPnP.PCL.Enum;
using ISSDP.UPnP.PCL.Interfaces.Model;
using ISSDP.UPnP.PCL.Interfaces.Service;
using SSDP.UPnP.PCL.Helper;
using SSDP.UPnP.PCL.Model;
using SSDP.UPnP.PCL.Service.Base;

namespace SSDP.UPnP.PCL.Service
{
    public class ControlPoint : CommonBase, IControlPoint
    {
        private readonly IHttpListener _httpListener;

        private IObservable<INotifySsdp> _notifyObs => Observable.Create<INotifySsdp>(
            obs =>
            {
                var disp = _httpListener
                    .HttpRequestObservable
                    .Where(x => !x.IsUnableToParseHttp && !x.IsRequestTimedOut)
                    .Where(req => req.Method == "NOTIFY")
                    .Select(req => new NotifySsdp(req))
                    .Where(n => n.NTS == NTS.Alive || n.NTS == NTS.ByeBye || n.NTS == NTS.Update)
                    .Subscribe(
                        obs.OnNext,
                        obs.OnError);

                return disp;
            }).Publish().RefCount();

        private IObservable<IMSearchResponse> _msearchResponse => Observable.Create<IMSearchResponse>(
            obs =>
            {
                var disp = _httpListener
                    .HttpResponseObservable
                    .Where(x => !x.IsUnableToParseHttp && !x.IsRequestTimedOut)
                    .Select(response => new MSearchResponse(response))
                    .Subscribe(
                        s =>
                        {
                            obs.OnNext(s);
                        });
                return disp;

            }).Publish().RefCount();

        public IObservable<INotifySsdp> NotifyObservable => _notifyObs;

        //public IObservable<INotifySsdp> NotifyObservable =>
        //    _httpListener
        //    .HttpRequestObservable
        //    .Where(x => !x.IsUnableToParseHttp && !x.IsRequestTimedOut)
        //    .Where(req => req.Method == "NOTIFY")
        //    .Select(req => new NotifySsdp(req))
        //    .Where(n => n.NTS == NTS.Alive || n.NTS == NTS.ByeBye || n.NTS == NTS.Update);

        public IObservable<IMSearchResponse> MSearchResponseObservable => _msearchResponse;

        //public IObservable<IMSearchResponse> MSearchResponseObservable =>
        //    _httpListener
        //    .HttpResponseObservable
        //    .Where(x => !x.IsUnableToParseHttp && !x.IsRequestTimedOut)
        //    .Select(response => new MSearchResponse(response));

        public ControlPoint(IHttpListener httpListener)
        {
            _httpListener = httpListener;
        }

        public async Task SendMSearchAsync(IMSearchRequest mSearch)
        {
            if (mSearch.SearchCastMethod == CastMethod.Multicast)
            {
                await _httpListener.SendOnMulticast(ComposeMSearchDatagram(mSearch));
            }

            if (mSearch.SearchCastMethod == CastMethod.Unicast)
            {
                await SendOnTcp(mSearch.HostIp, mSearch.HostPort, ComposeMSearchDatagram(mSearch));
            }
        }

        private static byte[] ComposeMSearchDatagram(IMSearchRequest request)
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.Append("M-SEARCH * HTTP/1.1\r\n");

            stringBuilder.Append(request.SearchCastMethod == CastMethod.Multicast
                ? "HOST: 239.255.255.250:1900\r\n"
                : $"HOST: {request.HostIp}:{request.HostPort}\r\n");

            stringBuilder.Append("MAN: \"ssdp:discover\"\r\n");

            if (request.SearchCastMethod == CastMethod.Multicast)
            {
                stringBuilder.Append($"MX: {request.MX.TotalSeconds}\r\n");
            }
            stringBuilder.Append($"ST: {request.ST}\r\n");
            stringBuilder.Append($"USER-AGENT: " +
                                 $"{request.UserAgent.OperatingSystem}/{request.UserAgent.OperatingSystemVersion}" +
                                 $" " +
                                 $"UPnP/{request.UserAgent.UpnpMajorVersion}.{request.UserAgent.UpnpMinorVersion}" +
                                 $" " +
                                 $"{request.UserAgent.ProductName}/{request.UserAgent.ProductVersion}\r\n");

            if (request.SearchCastMethod == CastMethod.Multicast)
            {
                stringBuilder.Append($"CPFN.UPNP.ORG: {request.CPFN}\r\n");

                HeaderHelper.AddOptionalHeader(stringBuilder, "TCPPORT.UPNP.ORG", request.TCPPORT);
                HeaderHelper.AddOptionalHeader(stringBuilder, "CPUUID.UPNP.ORG", request.CPUUID);

                if (request.Headers != null)
                {
                    foreach (var header in request.Headers)
                    {
                        stringBuilder.Append($"{header.Key}: {header.Value}\r\n");
                    }
                }
            }

            stringBuilder.Append("\r\n");
            return Encoding.UTF8.GetBytes(stringBuilder.ToString());
        }
    }
}