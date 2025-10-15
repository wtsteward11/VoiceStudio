# VoiceStudio Job Shapes (Core ⇄ Worker)
Envelope (Core passes to Run):
{
  ""Id"": ""VSJ-YYYYMMDDhhmmss"",
  ""Type"": ""Audio.Convert | Dataset.VAD | ASR.Transcribe | Align.Run | TTS.Synthesize | VC.Convert"",
  ""InPath"": ""<abs path or empty>"",
  ""OutPath"": ""<abs path or empty>"",
  ""ArgsJson"": ""<JSON string>""
}

Type specifics:
- Audio.Convert: InPath=raw\input.mp3, OutPath=processed\input.wav, ArgsJson {""ar"":48000,""ac"":1}
- Dataset.VAD:  InPath=processed\input.wav, OutPath=dataset\segments
- ASR.Transcribe: InPath=dataset\segments\manifest.json, OutPath=dataset\segments\asr.json, ArgsJson {""model"":""small"",""device"":""cpu""}
- Align.Run: InPath=dataset\segments\asr.json, OutPath=dataset\segments\aligned.json, ArgsJson {""lang"":""en"",""device"":""cpu"",""model"":""small""}
- TTS.Synthesize: OutPath=tts\hello.wav, ArgsJson {""text"":""..."",""voice"":""C:\\\\VoiceStudio\\\\tools\\\\piper\\\\voices\\\\en_US-amy-low.onnx""}
- VC.Convert: InPath=processed\input.wav, OutPath=vc\shifted.wav, ArgsJson {""semitones"":3}

Minimal return contract (Core.Run result):
- Success:  { ""status"":""ok"", ""outputs"": [""<path(s)>"" ]}
- Error:    { ""status"":""error"", ""code"":""E_*"", ""message"":""human text"" }

Error codes (normalize in Core):
- E_NO_INPUT, E_CONVERT_FAIL
- E_VAD_FAIL
- E_ASR_FAIL, E_ASR_MODEL_MISSING
- E_ALIGN_FAIL, E_ALIGN_MODEL_MISSING
- E_TTS_VOICE_MISSING, E_TTS_FAIL, E_PIPER_MISSING
- E_VC_FAIL, E_RVC_MODEL_MISSING
- E_NOT_IMPLEMENTED, E_UNHANDLED
