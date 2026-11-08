#!/usr/bin/env bash
set -euo pipefail
# Only official interfaces; callers may reuse an existing verified checkout.
revision=60629d14374c56f1cb51819049ad1fa529307f8d
destination=${1:-build/deps/dotnet-runtime}
if [[ ! -d "$destination/.git" ]]; then
  git clone --depth 1 --filter=blob:none --sparse --branch v10.0.0 \
    https://github.com/dotnet/runtime.git "$destination"
fi
[[ $(git -C "$destination" rev-parse HEAD) == "$revision" ]] || {
  echo 'Refusing unverified .NET interface checkout' >&2; exit 1;
}
git -C "$destination" sparse-checkout set src/coreclr/pal/prebuilt/inc \
  src/coreclr/pal/prebuilt/idl src/coreclr/pal/inc src/coreclr/inc src/native/minipal
printf 'Verified .NET v10.0.0 interfaces: %s\n' "$revision"
