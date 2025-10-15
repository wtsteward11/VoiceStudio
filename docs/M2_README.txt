VoiceStudio – M2 Alignment (word/phoneme timings) using WhisperX

What you get:
- align_requirements.txt  (packages to install in your venv)
- align_whisperx.py       (reads segments + asr.json, writes aligned.json with word timings)
- align_merge.py          (optional: merges manifest + asr + alignment into one dataset.json)

Steps (PowerShell):

1) Activate your existing venv
   cd C:\VoiceStudio\workers\python\vsdml
   . .\.venv\Scripts\Activate.ps1

2) Install alignment deps (CPU)
   pip install -r align_requirements.txt

   Notes:
   - This installs whisperx + torchaudio + torch (CPU).
   - First run downloads small alignment models; let it finish.

3) Run alignment
   python align_whisperx.py --segments "C:\VoiceStudio\projects\demo\dataset\segments" --lang en

   Output:
   - C:\VoiceStudio\projects\demo\dataset\segments\aligned.json

4) (Optional) Merge to one dataset file
   python align_merge.py --segments "C:\VoiceStudio\projects\demo\dataset\segments" --out "C:\VoiceStudio\projects\demo\dataset\dataset.json"

Troubleshooting:
- If torch complains about AVX or CPU features, switch to a smaller model or update Python to 3.11 (already recommended).
- If download fails, check your internet connection; alignment models are fetched on first use.
