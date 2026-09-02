// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Pure type witness pairing a Daml interface marker with its view record. Generated
/// markers expose a singleton through a static <c>View</c> property, so a call site
/// passing it to a generic method lets the compiler infer both type parameters from one
/// argument — C# performs no partial type-argument inference, so the pair must travel
/// together. A mismatched pair is unconstructible: the constraints tie
/// <typeparamref name="TView"/> to <typeparamref name="TInterface"/> through
/// <see cref="IHasView{TView}"/>.
/// </summary>
/// <typeparam name="TInterface">The Daml interface marker type.</typeparam>
/// <typeparam name="TView">The interface's view record type.</typeparam>
public sealed class ViewDescriptor<TInterface, TView>
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord<TView>
{
}
