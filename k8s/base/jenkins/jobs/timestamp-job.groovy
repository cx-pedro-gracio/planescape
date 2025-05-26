pipelineJob('record-timestamp') {
    description('Records the current date/time in the database every 5 minutes using a dynamic Kubernetes pod')
    triggers {
        cron('H/5 * * * *')
    }
    definition {
        cps {
            script("""
pipeline {
    agent {
        kubernetes {
            yaml '''
apiVersion: v1
kind: Pod
spec:
  containers:
  - name: psql
    image: bitnami/postgresql:latest
    command:
    - cat
    tty: true
    env:
    - name: PGPASSWORD
      valueFrom:
        secretKeyRef:
          name: jenkins-db-credentials
          key: password
    - name: PGUSER
      valueFrom:
        secretKeyRef:
          name: jenkins-db-credentials
          key: username
    - name: PGDATABASE
      valueFrom:
        secretKeyRef:
          name: jenkins-db-credentials
          key: database
    '''
        }
    }
    stages {
        stage('Record Timestamp') {
            steps {
                container('psql') {
                    sh '''
                    psql -h postgres -U $PGUSER -d $PGDATABASE -c "INSERT INTO job_timestamps (pod_name, namespace) VALUES ('$HOSTNAME', '$POD_NAMESPACE');"
                    '''
                }
            }
        }
    }
}
            """)
        }
    }
} 