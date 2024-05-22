// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

using Internal.Text;
using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    public enum ReadyToRunHelperId
    {
        Invalid,
        NewHelper,
        NewArr1,
        VirtualCall,
        IsInstanceOf,
        CastClass,
        GetNonGCStaticBase,
        GetGCStaticBase,
        GetThreadStaticBase,
        GetThreadNonGcStaticBase,
        DelegateCtor,
        ResolveVirtualFunction,
        CctorTrigger,

        // The following helpers are used for generic lookups only
        TypeHandle,
        NecessaryTypeHandle,
        DeclaringTypeHandle,
        MethodHandle,
        FieldHandle,
        MethodDictionary,
        TypeDictionary,
        MethodEntry,
        VirtualDispatchCell,
        DefaultConstructor,
        TypeHandleForCasting,
        ObjectAllocator,
        ConstrainedDirectCall,
    }

    public partial class ReadyToRunHelperNode : AssemblyStubNode
    {
        private readonly ReadyToRunHelperId _id;
        private readonly object _target;

        public ReadyToRunHelperNode(ReadyToRunHelperId id, object target)
        {
            _id = id;
            _target = target;

            switch (id)
            {
                case ReadyToRunHelperId.GetNonGCStaticBase:
                case ReadyToRunHelperId.GetGCStaticBase:
                case ReadyToRunHelperId.GetThreadStaticBase:
                    {
                        // Make sure we can compute static field layout now so we can fail early
                        DefType defType = (DefType)target;
                        defType.ComputeStaticFieldLayout(StaticLayoutKind.StaticRegionSizesAndFields);
                    }
                    break;
                case ReadyToRunHelperId.ResolveVirtualFunction:
                    {
                        // Make sure we aren't trying to callvirt Object.Finalize
                        MethodDesc method = (MethodDesc)target;
                        if (method.IsFinalizer)
                            ThrowHelper.ThrowInvalidProgramException(ExceptionStringID.InvalidProgramCallVirtFinalize, method);

                        // Method should be in fully canonical form. Otherwise we're being wasteful and generate more
                        // helpers than needed.
                        Debug.Assert(!method.IsCanonicalMethod(CanonicalFormKind.Any) ||
                            method.GetCanonMethodTarget(CanonicalFormKind.Specific) == method);
                    }
                    break;
            }
        }

        protected override bool IsVisibleFromManagedCode => false;

        protected override string GetName(NodeFactory factory) => this.GetMangledName(factory.NameMangler);

        public ReadyToRunHelperId Id => _id;
        public object Target =>  _target;

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            switch (_id)
            {
                case ReadyToRunHelperId.GetNonGCStaticBase:
                    sb.Append("__GetNonGCStaticBase_"u8).Append(nameMangler.GetMangledTypeName((TypeDesc)_target));
                    break;
                case ReadyToRunHelperId.GetGCStaticBase:
                    sb.Append("__GetGCStaticBase_"u8).Append(nameMangler.GetMangledTypeName((TypeDesc)_target));
                    break;
                case ReadyToRunHelperId.GetThreadStaticBase:
                    sb.Append("__GetThreadStaticBase_"u8).Append(nameMangler.GetMangledTypeName((TypeDesc)_target));
                    break;
                case ReadyToRunHelperId.DelegateCtor:
                    ((DelegateCreationInfo)_target).AppendMangledName(nameMangler, sb);
                    break;
                case ReadyToRunHelperId.ResolveVirtualFunction:
                    sb.Append("__ResolveVirtualFunction_"u8);
                    sb.Append(nameMangler.GetMangledMethodName((MethodDesc)_target));
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public bool IsStateMachineMoveNextMethod => false;

        public override bool IsShareable => true;

        protected override DependencyList ComputeNonRelocationBasedDependencies(NodeFactory factory)
        {
            if (_id == ReadyToRunHelperId.ResolveVirtualFunction)
            {
                var targetMethod = (MethodDesc)_target;

                DependencyList dependencyList = new DependencyList();

#if !SUPPORT_JIT
                factory.MetadataManager.GetDependenciesDueToVirtualMethodReflectability(ref dependencyList, factory, targetMethod);

                if (!factory.VTable(targetMethod.OwningType).HasKnownVirtualMethodUse)

                {
                    dependencyList.Add(factory.VirtualMethodUse((MethodDesc)_target), "ReadyToRun Virtual Method Call");
                }
#endif

                return dependencyList;
            }
            else if (_id == ReadyToRunHelperId.DelegateCtor)
            {
                DependencyList dependencyList = null;

                var info = (DelegateCreationInfo)_target;
                if (info.NeedsVirtualMethodUseTracking)
                {
                    MethodDesc targetMethod = info.TargetMethod;

#if !SUPPORT_JIT
                    factory.MetadataManager.GetDependenciesDueToVirtualMethodReflectability(ref dependencyList, factory, targetMethod);

                    if (!factory.VTable(info.TargetMethod.OwningType).HasKnownVirtualMethodUse)
                    {
                        dependencyList ??= new DependencyList();
                        dependencyList.Add(factory.VirtualMethodUse(info.TargetMethod), "ReadyToRun Delegate to virtual method");
                    }
#endif
                }

                return dependencyList;
            }

            return null;
        }

        public override bool HasConditionalStaticDependencies => _id == ReadyToRunHelperId.DelegateCtor;

        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory)
        {
            List<CombinedDependencyListEntry> dependencyList = new List<CombinedDependencyListEntry>();
            var info = (DelegateCreationInfo)_target;
            factory.MetadataManager.GetDependenciesDueToDelegateCreation(ref dependencyList, factory, info.DelegateType, info.PossiblyUnresolvedTargetMethod);
            return dependencyList;
        }

#if !SUPPORT_JIT
        public override int ClassCode => -911637948;

        public override int CompareToImpl(ISortableNode other, CompilerComparer comparer)
        {
            var compare = _id.CompareTo(((ReadyToRunHelperNode)other)._id);
            if (compare != 0)
                return compare;

            switch (_id)
            {
                case ReadyToRunHelperId.GetNonGCStaticBase:
                case ReadyToRunHelperId.GetGCStaticBase:
                case ReadyToRunHelperId.GetThreadStaticBase:
                    return comparer.Compare((TypeDesc)_target, (TypeDesc)((ReadyToRunHelperNode)other)._target);
                case ReadyToRunHelperId.ResolveVirtualFunction:
                    return comparer.Compare((MethodDesc)_target, (MethodDesc)((ReadyToRunHelperNode)other)._target);
                case ReadyToRunHelperId.DelegateCtor:
                    return ((DelegateCreationInfo)_target).CompareTo((DelegateCreationInfo)((ReadyToRunHelperNode)other)._target, comparer);
                default:
                    throw new NotImplementedException();
            }

        }
#endif
    }
}
