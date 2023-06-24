using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace CCTavern.Utility {
    public class DelayedMethodCaller {
        int   _delay;
        Timer _timer = new Timer();

        public DelayedMethodCaller(int delay) {
            _delay = delay;
        }

        public void CallMethod(Action action) {
            if (!_timer.Enabled) {
                _timer = new Timer(_delay) {
                    AutoReset = false
                };
                _timer.Elapsed += (_, _) => action();
                _timer.Start();
            } else {
                _timer.Stop();
                _timer.Start();
            }
        }

        public void Stop() {
            if (_timer.Enabled) {
                _timer.Stop();
            }
        }
    }
}
