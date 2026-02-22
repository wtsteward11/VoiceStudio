# Git push that works in Cursor agent environment.
# Cursor injects GITHUB_TOKEN (fine-grained PAT, no push). Unset it so git uses
# gh keyring token (classic PAT with repo scope).
$env:GITHUB_TOKEN = ''
$env:GITHUB_ACTIONS = ''
& git push @args
