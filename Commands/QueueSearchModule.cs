using Castle.Core.Logging;

using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    public class QueueSearchModule : BaseCommandModule {
        public ILogger<QueueSearchModule> logger { get; private set; }

        public QueueSearchModule(ILogger<QueueSearchModule> logger) {
            this.logger = logger;
        }
    }
}
