using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// 1PC base implementation of <see cref="ICacheTransactionManager"/>.
/// Mirrors cppcache <c>CacheTransactionManagerImpl</c>
/// (<c>cppcache/src/CacheTransactionManagerImpl.cpp</c>); the 2PC variant
/// <see cref="CacheTransactionManager2PC"/> extends this with
/// <c>PrepareAsync</c> + commit/rollback overrides.
/// </summary>
internal class CacheTransactionManager(IServiceProvider serviceProvider)
    : ICacheTransactionManager
{
    private readonly ILogger<CacheTransactionManager> _logger = serviceProvider.GetRequiredService<ILogger<CacheTransactionManager>>();
    private readonly List<int> _activeTxs = [];
    private readonly Lock _activeTxsLock = new();

    public void Begin()
    {
        if (TSSTXStateWrapper.Current != null)
        {
            throw new InvalidOperationException("Transaction already in progress");
        }
        var txState = new TXState();
        TSSTXStateWrapper.Current = txState;
        AddTx(txState.TransactionId.Id);
    }

    public virtual Task PrepareAsync(CancellationToken ct = default)
    {
        // TODO: cppcache 1PC base does not override prepare() — pure virtual on
        // CacheTransactionManager.hpp:80. Only CacheTransactionManager2PC provides it.
        throw new NotImplementedException();
    }

    public virtual Task CommitAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
        // cppcache CacheTransactionManagerImpl::commit (CacheTransactionManagerImpl.cpp:53-114):
        //
        //   void CacheTransactionManagerImpl::commit() {
        //     TXCleaner txCleaner(this);
        //     auto txState = txCleaner.getTXState();
        //
        //     if (txState == nullptr) {
        //       GfErrTypeThrowException(
        //           "Transaction is null, cannot commit a null transaction",
        //           GF_CACHE_ILLEGAL_STATE_EXCEPTION);
        //     }
        //
        //     TcrMessageCommit request(new DataOutput(m_cache->createDataOutput()));
        //     TcrMessageReply reply(true, nullptr);
        //
        //     auto tcr_dm = getDM();
        //     // This is for the case when no cache operation/s is performed between
        //     // tx->begin() and tx->commit()/rollback(),
        //     // simply return without sending COMMIT message to server. tcr_dm is nullptr
        //     // implies no cache operation is performed.
        //     // Theres no need to call txCleaner.clean(); here, because TXCleaner
        //     // destructor is called which cleans ThreadLocal.
        //     if (tcr_dm == nullptr) {
        //       return;
        //     }
        //
        //     GfErrType err = tcr_dm->sendSyncRequest(request, reply);
        //
        //     if (err != GF_NOERR) {
        //       // err = rollback(txState, false);
        //       //		noteCommitFailure(txState, nullptr);
        //       GfErrTypeThrowException("Error while committing", err);
        //     } else {
        //       switch (reply.getMessageType()) {
        //         case TcrMessage::RESPONSE: {
        //           break;
        //         }
        //         case TcrMessage::EXCEPTION: {
        //           //			noteCommitFailure(txState, nullptr);
        //           const auto& exceptionMsg = reply.getException();
        //           err = ThinClientRegion::handleServerException(
        //               "CacheTransactionManager::commit", exceptionMsg);
        //           GfErrTypeThrowException("Commit Failed", err);
        //           break;
        //         }
        //         case TcrMessage::COMMIT_ERROR: {
        //           //			noteCommitFailure(txState, nullptr);
        //           GfErrTypeThrowException("Commit Failed", GF_COMMIT_CONFLICT_EXCEPTION);
        //           break;
        //         }
        //         default: {
        //           //			noteCommitFailure(txState, nullptr);
        //           LOGERROR("Unknown message type in commit reply %d",
        //                    reply.getMessageType());
        //           GfErrTypeThrowException("Commit Failed", GF_MSG);
        //           break;
        //         }
        //       }
        //     }
        //
        //     auto commit = std::dynamic_pointer_cast<TXCommitMessage>(reply.getValue());
        //     txCleaner.clean();
        //     commit->apply(m_cache->getCache());
        //   }
    }

    public virtual Task RollbackAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
        // cppcache CacheTransactionManagerImpl::rollback (CacheTransactionManagerImpl.cpp:116-137):
        //
        //   void CacheTransactionManagerImpl::rollback() {
        //     TXCleaner txCleaner(this);
        //     TXState* txState = txCleaner.getTXState();
        //
        //     if (txState == nullptr) {
        //       GfErrTypeThrowException("Thread does not have an active transaction",
        //                               GF_CACHE_ILLEGAL_STATE_EXCEPTION);
        //     }
        //
        //     try {
        //       GfErrType err = rollback(txState, true);   // ← private helper below
        //       if (err != GF_NOERR) {
        //         throwExceptionIfError("Error while committing", err);
        //       }
        //     } catch (const Exception& ex) {
        //       // TODO: put a log message
        //       throw ex;
        //     } catch (...) {
        //       // TODO: put a log message
        //       throw;
        //     }
        //   }
    }

    private Task RollbackAsync(TXState txState, bool callListener)
    {
        throw new NotImplementedException();
        // cppcache CacheTransactionManagerImpl::rollback private helper (CacheTransactionManagerImpl.cpp:139-177):
        //
        //   GfErrType CacheTransactionManagerImpl::rollback(TXState*, bool) {
        //     TcrMessageRollback request(new DataOutput(m_cache->createDataOutput()));
        //     TcrMessageReply reply(true, nullptr);
        //     GfErrType err = GF_NOERR;
        //     ThinClientPoolDM* tcr_dm = getDM();
        //     // This is for the case when no cache operation/s is performed between
        //     // tx->begin() and tx->commit()/rollback(),
        //     // simply return without sending COMMIT message to server. tcr_dm is nullptr
        //     // implies no cache operation is performed.
        //     // Theres no need to call txCleaner.clean(); here, because TXCleaner
        //     // destructor is called which cleans ThreadLocal.
        //     if (tcr_dm == nullptr) {
        //       return err;
        //     }
        //     err = tcr_dm->sendSyncRequest(request, reply);
        //
        //     if (err == GF_NOERR) {
        //       switch (reply.getMessageType()) {
        //         case TcrMessage::REPLY: {
        //           break;
        //         }
        //         case TcrMessage::EXCEPTION: {
        //           break;
        //         }
        //         default: {
        //           break;
        //         }
        //       }
        //     }
        //
        //     /*	if(err == GF_NOERR && callListener)
        //             {
        //     //	auto commit =
        //     std::static_pointer_cast<TXCommitMessage>(reply.getValue());
        //                     noteRollbackSuccess(txState, nullptr);
        //             }
        //     */
        //     return err;
        //   }
        //
        // Note: both args (txState, callListener) are dead in cppcache's current body —
        // txState ignored, callListener only used in commented-out listener block.
        // Signature preserved for parity / future listener wiring.
    }

    protected ThinClientBaseDM? GetDM()
    {
        throw new NotImplementedException();
        // cppcache CacheTransactionManagerImpl::getDM (CacheTransactionManagerImpl.cpp:179-186):
        //
        //   ThinClientPoolDM* CacheTransactionManagerImpl::getDM() {
        //     if (auto conn = TssConnectionWrapper::get().getConnection()) {
        //       if (auto dm = conn->getEndpointObject()->getPoolHADM()) {
        //         return dm;
        //       }
        //     }
        //     return nullptr;
        //   }
        //
        // Peek thread-sticky TcrConnection (cppcache TssConnectionWrapper, C# DmContextAccessor),
        // walk to its endpoint + pool DM. null when no sticky connection exists —
        // i.e. no cache op happened in this tx yet, so commit/rollback skip the wire.
    }

    public ITransactionId Suspend()
    {
        // TODO: cppcache CacheTransactionManagerImpl::suspend (CacheTransactionManagerImpl.cpp:189)
        throw new NotImplementedException();
    }

    public Task ResumeAsync(ITransactionId transactionId, CancellationToken ct = default)
    {
        // TODO: cppcache CacheTransactionManagerImpl::resume (CacheTransactionManagerImpl.cpp:239)
        throw new NotImplementedException();
    }

    public Task<bool> TryResumeAsync(ITransactionId transactionId, CancellationToken ct = default)
    {
        // TODO: cppcache CacheTransactionManagerImpl::tryResume (CacheTransactionManagerImpl.cpp:261)
        throw new NotImplementedException();
    }

    public Task<bool> TryResumeAsync(ITransactionId transactionId, TimeSpan waitTime, CancellationToken ct = default)
    {
        // TODO: cppcache CacheTransactionManagerImpl::tryResume(waitTime) (CacheTransactionManagerImpl.cpp:281)
        throw new NotImplementedException();
    }

    public bool IsSuspended(ITransactionId transactionId)
    {
        // TODO: cppcache CacheTransactionManagerImpl::isSuspended (CacheTransactionManagerImpl.cpp:258)
        throw new NotImplementedException();
    }

    public bool Exists(ITransactionId transactionId)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        var txId = (TXId)transactionId;
        return FindTx(txId.Id);
    }

    public bool Exists() => TSSTXStateWrapper.Current is not null;

    public ITransactionId? TransactionId => TSSTXStateWrapper.Current?.TransactionId;

    private void AddTx(int txId)
    {
        lock (_activeTxsLock)
        {
            _activeTxs.Add(txId);
        }
    }

    private bool FindTx(int txId)
    {
        lock (_activeTxsLock)
        {
            return _activeTxs.Contains(txId);
        }
    }
    public void RemoveTx(int id)
    {
        lock (_activeTxsLock)
        {
            _activeTxs.Remove(id);
        }
    }
}
