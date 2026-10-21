pipeline {
    agent any

    options {
        skipDefaultCheckout(true)
        disableConcurrentBuilds()
        timeout(time: 90, unit: 'MINUTES')
    }

    environment {
        UNITY_EXE = 'E:\\Unity Editor\\6000.0.79f1\\Editor\\Unity.exe'
    }

    stages {
        stage('Checkout') {
            steps {
                script {
                    if (!isUnix()) {
                        def revision = git branch: 'main',
                            url: 'https://github.com/chocolate-cat-233/FYP_2.git'
                        env.GIT_COMMIT = revision.GIT_COMMIT
                        env.CI_BUILD_OUTPUT = "${env.WORKSPACE}\\Builds\\CI-${env.BUILD_NUMBER}\\Windows64"
                    } else {
                        error('This pipeline requires a Windows agent with Unity 6000.0.79f1.')
                    }
                }
            }
        }

        stage('Preflight') {
            steps {
                powershell '''
$ErrorActionPreference = 'Stop'
whoami
if (!(Test-Path -LiteralPath $env:UNITY_EXE -PathType Leaf)) {
    throw 'Unity.exe not found.'
}
$version = Get-Content -LiteralPath 'ProjectSettings/ProjectVersion.txt'
if ($version -notcontains 'm_EditorVersion: 6000.0.79f1') {
    throw 'Project must use Unity 6000.0.79f1.'
}
if (Test-Path -LiteralPath $env:CI_BUILD_OUTPUT) {
    throw 'Build output already exists. Start a new build instead of reusing output.'
}
Write-Output "Commit: $env:GIT_COMMIT"
Write-Output "Output: $env:CI_BUILD_OUTPUT"
'''
            }
        }

        stage('Build Windows64') {
            steps {
                bat '''
@echo off
"%UNITY_EXE%" -batchmode -quit -buildTarget Win64 -projectPath "%WORKSPACE%" -executeMethod Fyp.CI.BuildScript.BuildWindows64 -logFile "%WORKSPACE%\\unity-build-%BUILD_NUMBER%.log"
set "UNITY_EXIT_CODE=%ERRORLEVEL%"
echo Unity exit code: %UNITY_EXIT_CODE%
exit /b %UNITY_EXIT_CODE%
'''
            }
        }

        stage('Package Player') {
            steps {
                powershell '''
$ErrorActionPreference = 'Stop'
foreach ($required in @('SuperFinalFuse.exe', 'SuperFinalFuse_Data', 'UnityPlayer.dll', 'build-info.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $env:CI_BUILD_OUTPUT $required))) {
        throw "Missing build output: $required"
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $env:WORKSPACE "Builds/CI-$env:BUILD_NUMBER/SuperFinalFuse-Windows64-$env:BUILD_NUMBER.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory($env:CI_BUILD_OUTPUT, $zip)
Write-Output "Packaged complete Player directory: $zip"
'''
                archiveArtifacts artifacts: "Builds/CI-${env.BUILD_NUMBER}/*.zip", fingerprint: true
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: "unity-build-${env.BUILD_NUMBER}.log", allowEmptyArchive: true
        }
    }
}
