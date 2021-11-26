using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DxgiLatencyShare
{
    public class InterProcessCommunication : IDisposable
    {
        public const string DxgiLatencyProviderTitle = "DxgiLatencyProvider";

        public EventWaitHandle StartRenderEvent { get; private set; }
        public EventWaitHandle StopRenderEvent { get; private set; }
        public EventWaitHandle SavedImageEvent { get; private set; }

        private const string startRenderEventName = "DxgiLatencyStartRender";
        private const string stopRenderEventName = "DxgiLatencyStopRender";
        private const string savedImageEventName = "DxgiLatencySavedImage";

        public InterProcessCommunication()
        {
            StartRenderEvent = NamedEvent.CreateOrOpenNamedEvent(startRenderEventName, false, true);
            StopRenderEvent = NamedEvent.CreateOrOpenNamedEvent(stopRenderEventName, false, true);
            SavedImageEvent = NamedEvent.CreateOrOpenNamedEvent(savedImageEventName, false, true);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (StartRenderEvent != null)
                    {
                        StartRenderEvent.Dispose();
                    }

                    if (StopRenderEvent != null)
                    {
                        StopRenderEvent.Dispose();
                    }

                    if (SavedImageEvent != null)
                    {
                        SavedImageEvent.Dispose();
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~InterProcessCommunication() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion


    }
}
