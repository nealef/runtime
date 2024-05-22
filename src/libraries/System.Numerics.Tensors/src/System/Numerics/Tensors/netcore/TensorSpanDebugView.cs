﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Numerics.Tensors
{
    internal sealed class TensorSpanDebugView<T>
    {
        private readonly T[] _array;

        public TensorSpanDebugView(TensorSpan<T> span)
        {
            _array = new T[span.FlattenedLength];
            span.FlattenTo(_array);
        }

        public TensorSpanDebugView(ReadOnlyTensorSpan<T> span)
        {
            _array = new T[span.FlattenedLength];
            span.FlattenTo(_array);
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items => _array;
    }
}
