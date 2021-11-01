using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace MarieAmp
{
    public class VM_MainWindow: ObservableObject
    {
        
        StreamMP3Reader _mp3;
        public double m_Buffer { get
            {
                if(_mp3 == null)
                {
                    return 0.0;
                }
                return _mp3.m_dBuffer_second;
            } }

        private ICommand _cmdPlay;

        public ICommand m_cmdPlay
        {
            get
            {
                if (_cmdPlay == null)
                {
                    _cmdPlay = new RelayCommand((o) =>
                    {
                        _mp3 = new StreamMP3Reader();
                        _mp3.PropertyChanged += _mp3_PropertyChanged;
                        _mp3.streamMe(@"http://listen.radioking.com/radio/563/stream/62872");
                    

                    }, o => true);
                }
                return _cmdPlay;
            }
        }

    

        private ICommand _cmdStop;

        public ICommand m_cmdStop
        {
            get
            {
                if (_cmdStop == null)
                {
                    _cmdStop = new RelayCommand((o) =>
                    {
                        _mp3.Stop();


                    }, o => true);
                }
                return _cmdStop;
            }
        }

        private void _mp3_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            NotifyPropertyChanged(nameof(m_Buffer));
        }
    }
}
