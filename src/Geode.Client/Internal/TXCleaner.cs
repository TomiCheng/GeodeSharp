using System;
using System.Collections.Generic;
using System.Text;

namespace Geode.Client.Internal;

internal struct TXCleaner(CacheTransactionManager manager) : IDisposable
{
    public void Dispose() => throw new NotImplementedException();

    public readonly void Clean()
    {
        var txState = TSSTXStateWrapper.Current;
        if (txState is not null)
        {
            manager.RemoveTx(txState.TransactionId.Id);
        }
        if (txState is not null)
        {
            TSSTXStateWrapper.Current = null;
        }
    }
}
