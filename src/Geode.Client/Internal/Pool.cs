using System;
using System.Collections.Generic;
using System.Text;

namespace Geode.Client.Internal;

internal class Pool(PoolAttributes attributes)
    : IPool
{
    PoolAttributes _ = attributes;
    public ValueTask DisposeAsync() => throw new NotImplementedException();
}
