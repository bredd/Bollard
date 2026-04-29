using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;
internal interface IAssetBuilder {

    /// <summary>
    /// Build the asset
    /// </summary>
    /// <remarks>
    /// <para>If the asset requres or allows arguments to be set, they
    /// should be set using constructor, properties, or methods calling
    /// ProduceAsync().
    /// </para>
    /// <para>This should produce the final asset. For example, if a Razor asset
    /// references a layout then the asset should make the nested call to do the
    /// layout before produce finishes.
    /// </para>
    /// <para> This is separate from <see cref="DeliverAsync"/> because an asset might
    /// be produced once but be delivered multiple times. Also, overloads of
    /// Deliver in subclasses might allow the asset to be delivered in multiple
    /// formats.
    /// </para>
    /// </remarks>
    void Produce();

    /// <summary>
    /// The default delivery is to a stream.
    /// </summary>
    /// <param name="stream">An open stream to which the asset should be delivered. Typically a <see cref="FileStream"/>.</param>
    void Deliver(Stream stream);

}
