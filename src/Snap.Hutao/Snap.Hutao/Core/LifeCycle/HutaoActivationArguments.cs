// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.Windows.AppLifecycle;

namespace Snap.Hutao.Core.LifeCycle;

internal sealed class HutaoActivationArguments
{
    public bool IsRedirectTo { get; private set; }

    public HutaoActivationKind Kind { get; private set; }

    public Uri? ProtocolActivatedUri { get; private set; }

    public string? LaunchActivatedArguments { get; private set; }

    public IReadOnlyDictionary<string, string>? AppNotificationActivatedArguments { get; private set; }

    public IReadOnlyDictionary<string, string>? AppNotificationActivatedUserInput { get; private set; }

    public static HutaoActivationArguments FromAppActivationArguments(AppActivationArguments args, bool isRedirected = false)
    {
        HutaoActivationArguments result = new()
        {
            IsRedirectTo = isRedirected,
        };

        switch (args.Kind)
        {
            case ExtendedActivationKind.Launch:
                {
                    result.Kind = HutaoActivationKind.Launch;
                    if (args.TryGetLaunchActivatedArguments(out string? arguments))
                    {
                        result.LaunchActivatedArguments = arguments;
                    }

                    break;
                }

            case ExtendedActivationKind.Protocol:
                {
                    result.Kind = HutaoActivationKind.Protocol;
                    if (args.TryGetProtocolActivatedUri(out Uri? uri))
                    {
                        result.ProtocolActivatedUri = uri;
                    }

                    break;
                }

            case ExtendedActivationKind.AppNotification:
                {
                    result.Kind = HutaoActivationKind.AppNotification;
                    if (args.TryGetAppNotificationActivatedArguments(out string? argument, out IDictionary<string, string>? arguments, out IDictionary<string, string>? userInput))
                    {
                        result.LaunchActivatedArguments = argument;
                        result.AppNotificationActivatedArguments = arguments.AsReadOnly();
                        result.AppNotificationActivatedUserInput = userInput.AsReadOnly();
                    }

                    break;
                }
        }

        return result;
    }
}