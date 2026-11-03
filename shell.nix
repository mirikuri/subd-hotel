{ pkgs ? import <nixpkgs> {} }:
pkgs.mkShell {
  buildInputs = [
    pkgs.dotnet-sdk_8
    pkgs.postgresql 
  ];
  shellHook = ''
    export DOTNET_CLI_TELEMETRY_OPTOUT=1
  '';
}
