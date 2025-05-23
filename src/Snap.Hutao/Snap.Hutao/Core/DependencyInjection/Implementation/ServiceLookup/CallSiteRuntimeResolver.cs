// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using System.Diagnostics;
using System.Reflection;

namespace Snap.Hutao.Core.DependencyInjection.Implementation.ServiceLookup;

internal sealed class CallSiteRuntimeResolver : CallSiteVisitor<RuntimeResolverContext, object?>
{
    private CallSiteRuntimeResolver()
    {
    }

    public static CallSiteRuntimeResolver Instance { get; } = new();

    public object? Resolve(ServiceCallSite callSite, ServiceProviderEngineScope scope)
    {
        // Fast path to avoid virtual calls if we already have the cached value in the root scope
        if (scope.IsRootScope && callSite.Value is { } cached)
        {
            return cached;
        }

        return VisitCallSite(callSite, new RuntimeResolverContext
        {
            Scope = scope,
        });
    }

    protected override object? VisitDisposeCache(ServiceCallSite transientCallSite, RuntimeResolverContext context)
    {
        return context.Scope.CaptureDisposable(VisitCallSiteMain(transientCallSite, context));
    }

    protected override object VisitConstructor(ConstructorCallSite constructorCallSite, RuntimeResolverContext context)
    {
        object?[] parameterValues;
        if (constructorCallSite.ParameterCallSites.Length == 0)
        {
            parameterValues = [];
        }
        else
        {
            parameterValues = new object?[constructorCallSite.ParameterCallSites.Length];
            for (int index = 0; index < parameterValues.Length; index++)
            {
                parameterValues[index] = VisitCallSite(constructorCallSite.ParameterCallSites[index], context);
            }
        }

        return constructorCallSite.ConstructorInfo.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, parameters: parameterValues, culture: null);
    }

    protected override object? VisitRootCache(ServiceCallSite callSite, RuntimeResolverContext context)
    {
        if (callSite.Value is { } value)
        {
            // Value already calculated, return it directly
            return value;
        }

        RuntimeResolverLock lockType = RuntimeResolverLock.Root;
        ServiceProviderEngineScope serviceProviderEngine = context.Scope.RootProvider.Root;

        lock (callSite)
        {
            // Lock the callsite and check if another thread already cached the value
            if (callSite.Value is { } callSiteValue)
            {
                return callSiteValue;
            }

            object? resolved = VisitCallSiteMain(callSite, new RuntimeResolverContext
            {
                Scope = serviceProviderEngine,
                AcquiredLocks = context.AcquiredLocks | lockType,
            });
            serviceProviderEngine.CaptureDisposable(resolved);
            callSite.Value = resolved;
            return resolved;
        }
    }

    protected override object? VisitScopeCache(ServiceCallSite callSite, RuntimeResolverContext context)
    {
        // Check if we are in the situation where scoped service was promoted to singleton
        // and we need to lock the root
        return context.Scope.IsRootScope ?
            VisitRootCache(callSite, context) :
            VisitCache(callSite, context, context.Scope, RuntimeResolverLock.Scope);
    }

    protected override object? VisitConstant(ConstantCallSite constantCallSite, RuntimeResolverContext context)
    {
        return constantCallSite.DefaultValue;
    }

    protected override object VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, RuntimeResolverContext context)
    {
        return context.Scope;
    }

    protected override object VisitIEnumerable(IEnumerableCallSite enumerableCallSite, RuntimeResolverContext context)
    {
        Array array = CreateArray(enumerableCallSite.ItemType, enumerableCallSite.ServiceCallSites.Length);

        for (int index = 0; index < enumerableCallSite.ServiceCallSites.Length; index++)
        {
            object? value = VisitCallSite(enumerableCallSite.ServiceCallSites[index], context);
            array.SetValue(value, index);
        }

        return array;

        [UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "VerifyAotCompatibility ensures elementType is not a ValueType")]
        static Array CreateArray(Type elementType, int length)
        {
            Debug.Assert(!ServiceProvider.VerifyAotCompatibility || !elementType.IsValueType, "VerifyAotCompatibility=true will throw during building the IEnumerableCallSite if elementType is a ValueType.");

            return Array.CreateInstance(elementType, length);
        }
    }

    protected override object VisitFactory(FactoryCallSite factoryCallSite, RuntimeResolverContext context)
    {
        return factoryCallSite.Factory(context.Scope);
    }

    private object? VisitCache(ServiceCallSite callSite, RuntimeResolverContext context, ServiceProviderEngineScope serviceProviderEngine, RuntimeResolverLock lockType)
    {
        bool lockTaken = false;
        object sync = serviceProviderEngine.Sync;
        Dictionary<ServiceCacheKey, object?> resolvedServices = serviceProviderEngine.ResolvedServices;

        // Taking locks only once allows us to fork resolution process
        // on another thread without causing the deadlock because we
        // always know that we are going to wait the other thread to finish before
        // releasing the lock
        if ((context.AcquiredLocks & lockType) == 0)
        {
            Monitor.Enter(sync, ref lockTaken);
        }

        try
        {
            // Note: This method has already taken lock by the caller for resolution and access synchronization.
            // For scoped: takes a dictionary as both a resolution lock and a dictionary access lock.
            if (resolvedServices.TryGetValue(callSite.Cache.Key, out object? resolved))
            {
                return resolved;
            }

            resolved = VisitCallSiteMain(callSite, new RuntimeResolverContext
            {
                Scope = serviceProviderEngine,
                AcquiredLocks = context.AcquiredLocks | lockType,
            });
            serviceProviderEngine.CaptureDisposable(resolved);
            resolvedServices.Add(callSite.Cache.Key, resolved);
            return resolved;
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(sync);
            }
        }
    }
}