using System;
using System.Collections.Generic;
using System.Text;

namespace Geode.Client.Internal;

internal enum CommitOp
{
    BeforeCommit = 0,   // PrepareAsync 用
    AfterCommit = 1,    // CommitAsync / RollbackAsync 用
}
