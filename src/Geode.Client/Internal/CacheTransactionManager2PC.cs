using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// 2PC variant of <see cref="CacheTransactionManager"/>. Mirrors cppcache
/// <c>InternalCacheTransactionManager2PCImpl</c>
/// (<c>cppcache/src/InternalCacheTransactionManager2PCImpl.cpp</c>).
/// </summary>
/// <remarks>
/// Dual-mode: if <see cref="PrepareAsync"/> was called the tx state is
/// marked prepared and commit/rollback send a <c>TcrMessageTxSynchronization</c>
/// AFTER_COMMIT; otherwise <see cref="CommitAsync"/> / <see cref="RollbackAsync"/>
/// fall through to the 1PC base impl.
/// </remarks>
internal sealed class CacheTransactionManager2PC(IServiceProvider serviceProvider)
    : CacheTransactionManager(serviceProvider)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<CacheTransactionManager2PC> _logger
        = serviceProvider.GetRequiredService<ILogger<CacheTransactionManager2PC>>();
    public override Task PrepareAsync(CancellationToken ct = default)
    {
        // TODO: cppcache InternalCacheTransactionManager2PCImpl::prepare
        //       (InternalCacheTransactionManager2PCImpl.cpp:43)
        //       — sends TcrMessageTxSynchronization(BEFORE_COMMIT) + marks TxState prepared
        throw new NotImplementedException();
    }

    public override async Task CommitAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("Committing");
        await AfterCompletionAsync(TxCompletionStatus.Committed, ct);
    }

    public override async Task RollbackAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("Rolling back");
        await AfterCompletionAsync(TxCompletionStatus.RolledBack, ct);
    }

    private async Task AfterCompletionAsync(TxCompletionStatus status, CancellationToken ct = default)
    {
        try
        {
            var txState = TSSTXStateWrapper.Current
                ?? throw new GfErrTypeException(GfErrType.CacheIllegalStateException, "Transaction is null, cannot commit a null transaction");

            var tcr_dm = txState.DM;
            // This is for the case when no cache operation/s is performed between
            // tx->begin() and tx->commit()/rollback(),
            // simply return without sending COMMIT message to server. tcr_dm is nullptr
            // implies no cache operation is performed.
            // Theres no need to call txCleaner.clean(); here, because TXCleaner
            // destructor is called which cleans ThreadLocal.
            if (tcr_dm is null)
            {
                using TXCleaner _0 = new(this);
                return;
            }

            if (!txState.IsPrepared)
            {
                // Fallback to default 1PC commit
                // The inherited 1PC implementation clears the transaction state
                switch (status)
                {
                    case TxCompletionStatus.Committed:
                        await base.CommitAsync(ct);
                        break;
                    case TxCompletionStatus.RolledBack:
                        await base.RollbackAsync(ct);
                        break;
                    default:
                        throw new GfErrTypeException(GfErrType.CacheIllegalStateException, "Unknown command");
                }
                return;
            }

            // In 2PC we always clear the transaction state
            using TXCleaner txCleaner = new(this);
            var requestCommitAfter = await TcrMessageBuilder
                .Create(_serviceProvider, MessageType.TxSynchronization)
                .AddInt32Part((int)CommitOp.AfterCommit)
                .AddInt32Part(txState.TransactionId.Id)
                .AddInt32Part((int)status)
                .BuildAsync(ct);

            TcrMessage replyCommitAfter;
            try
            {
                replyCommitAfter = await tcr_dm.SendSyncRequestAsync(requestCommitAfter, ct: ct);
            }
            catch (GfErrTypeException ex)
            {
                throw new GfErrTypeException(ex.Code, "Error in 2PC commit", ex);
            }
            switch (replyCommitAfter.MessageType)
            {
                case MessageType.Response:
                    var commit = replyCommitAfter.GetValue<TXCommitMessage>();
                    if (commit is not null)
                    {
                        // e.g. when afterCompletion(STATUS_ROLLEDBACK) called
                        txCleaner.Clean();
                        var cache = _serviceProvider.GetRequiredService<GeodeCache>();
                        commit.Apply(cache);
                    }
                    break;
                case MessageType.Exception:
                    var exceptionMsg = replyCommitAfter.GetException();
                    var errCode = ThinClientRegion.HandleServerException(_logger, "CacheTransactionManager::afterCompletion", exceptionMsg);
                    throw new GfErrTypeException(errCode, "2PC Commit Failed");
                case MessageType.RequestDataError:
                    throw new GfErrTypeException(GfErrType.CommitConflictException, "2PC Commit Failed");
                default:
                    throw new GfErrTypeException(GfErrType.Msg, "2PC Commit Failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception during completing transaction");
            throw;
        }
    }
}
