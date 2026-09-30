pipeline {
    agent any

    options {
        disableConcurrentBuilds()
        timeout(time: 20, unit: 'MINUTES')
    }

    triggers {
        pollSCM('H/2 * * * *')
    }

    stages {
        stage('Deploy') {
            steps {
                // -p hakutaku keeps the compose project name the same as the manual deploy,
                // so the existing pgdata and caddy_data volumes are reused, not recreated.
                sh 'docker compose -p hakutaku --env-file /var/lib/jenkins/hakutaku.env -f compose.yaml up -d --build'

                // Compose won't restart caddy when only the Caddyfile's contents change,
                // and caddy doesn't watch the file. reload validates the new config first
                // and keeps the running one if it's invalid, so this fails the build
                // rather than the site.
                sh 'docker compose -p hakutaku --env-file /var/lib/jenkins/hakutaku.env -f compose.yaml exec -T caddy caddy reload --config /etc/caddy/Caddyfile'
            }
        }

        stage('Smoke test') {
            steps {
                sh 'curl -fsS --retry 15 --retry-delay 4 --retry-connrefused https://51.79.242.169.nip.io/Health'
            }
        }
    }
}
