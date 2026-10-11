#!/bin/bash
set -eo pipefail

echo "=========================================================="
echo "  [Alert Pulse] Deploying AWS AppSync GraphQL Real-Time API"
echo "=========================================================="

REGION="${AWS_DEFAULT_REGION:-us-east-1}"
API_NAME="${APPSYNC_API_NAME:-AlertPulseApi}"
TABLE_NAME="${DYNAMODB_TABLE_NAME:-MonitoringHub}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA_PATH="$SCRIPT_DIR/schema.graphql"
RESOLVERS_DIR="$SCRIPT_DIR/resolvers"

echo "--> Target Region: $REGION"
echo "--> API Name:      $API_NAME"
echo "--> DynamoDB Table:$TABLE_NAME"

# 1. Create GraphQL API
echo "--> Creating AppSync GraphQL API..."
API_ID=$(aws appsync create-graphql-api \
    --region "$REGION" \
    --name "$API_NAME" \
    --authentication-type API_KEY \
    --query "graphqlApi.apiId" \
    --output text)

echo "    Created API ID: $API_ID"

# 2. Create API Key (Valid for 365 days / 1 year)
EXPIRES=$(date -v+365d +%s 2>/dev/null || date -d "+365 days" +%s)
echo "--> Generating API Key (Expires: $(date -r $EXPIRES 2>/dev/null || date -d @$EXPIRES))..."
API_KEY=$(aws appsync create-api-key \
    --region "$REGION" \
    --api-id "$API_ID" \
    --description "Key for Alert Pulse Angular Frontend & .NET Webhook Dispatcher" \
    --expires "$EXPIRES" \
    --query "apiKey.id" \
    --output text)

echo "    Generated API Key: $API_KEY"

# 3. Upload & Deploy GraphQL Schema
echo "--> Uploading GraphQL schema ($SCHEMA_PATH)..."
aws appsync start-schema-creation \
    --region "$REGION" \
    --api-id "$API_ID" \
    --definition fileb://"$SCHEMA_PATH"

echo "--> Waiting for schema compilation..."
while true; do
    STATUS=$(aws appsync get-schema-creation-status \
        --region "$REGION" \
        --api-id "$API_ID" \
        --query "status" \
        --output text)
    if [ "$STATUS" = "SUCCESS" ]; then
        echo "    Schema deployed successfully!"
        break
    elif [ "$STATUS" = "FAILED" ]; then
        DETAILS=$(aws appsync get-schema-creation-status --region "$REGION" --api-id "$API_ID" --query "details" --output text)
        echo "    Schema deployment FAILED: $DETAILS"
        exit 1
    fi
    sleep 2
done

# 4. Create None Data Source (Local Passthrough Pub/Sub)
echo "--> Creating NoneDataSource (Local Passthrough)..."
aws appsync create-data-source \
    --region "$REGION" \
    --api-id "$API_ID" \
    --name "NoneDataSource" \
    --type NONE

# 5. Create IAM Service Role for DynamoDB DataSource
echo "--> Ensuring AppSync IAM Service Role for DynamoDB..."
ROLE_NAME="AlertPulseAppSyncDynamoDbRole"
ACCOUNT_ID=$(aws sts get-caller-identity --query "Account" --output text 2>/dev/null || echo "000000000000")
ROLE_ARN="arn:aws:iam::${ACCOUNT_ID}:role/${ROLE_NAME}"

if ! aws iam get-role --role-name "$ROLE_NAME" >/dev/null 2>&1; then
    TRUST_POLICY='{
        "Version": "2012-10-17",
        "Statement": [{
            "Effect": "Allow",
            "Principal": { "Service": "appsync.amazonaws.com" },
            "Action": "sts:AssumeRole"
        }]
    }'
    ROLE_ARN=$(aws iam create-role \
        --role-name "$ROLE_NAME" \
        --assume-role-policy-document "$TRUST_POLICY" \
        --query "Role.Arn" \
        --output text)

    DDB_POLICY='{
        "Version": "2012-10-17",
        "Statement": [{
            "Effect": "Allow",
            "Action": [
                "dynamodb:GetItem",
                "dynamodb:PutItem",
                "dynamodb:UpdateItem",
                "dynamodb:Query"
            ],
            "Resource": [
                "arn:aws:dynamodb:'"$REGION"':'"$ACCOUNT_ID"':table/'"$TABLE_NAME"'",
                "arn:aws:dynamodb:'"$REGION"':'"$ACCOUNT_ID"':table/'"$TABLE_NAME"'/index/*"
            ]
        }]
    }'
    aws iam put-role-policy \
        --role-name "$ROLE_NAME" \
        --policy-name "AppSyncDynamoDbAccess" \
        --policy-document "$DDB_POLICY"
fi

# 6. Create DynamoDB Data Source
echo "--> Creating DynamoDBDataSource ($TABLE_NAME)..."
aws appsync create-data-source \
    --region "$REGION" \
    --api-id "$API_ID" \
    --name "DynamoDBDataSource" \
    --type AMAZON_DYNAMODB \
    --service-role-arn "$ROLE_ARN" \
    --dynamodb-config "tableName=$TABLE_NAME,awsRegion=$REGION"

# 7. Attach Resolvers
echo "--> Attaching Resolver: Mutation.publishWebhookEvent..."
aws appsync create-resolver \
    --region "$REGION" \
    --api-id "$API_ID" \
    --type-name "Mutation" \
    --field-name "publishWebhookEvent" \
    --data-source-name "NoneDataSource" \
    --request-mapping-template file://"$RESOLVERS_DIR/Mutation.publishWebhookEvent.req.vtl" \
    --response-mapping-template file://"$RESOLVERS_DIR/Mutation.publishWebhookEvent.res.vtl"

echo "--> Attaching Resolver: Mutation.updateEventStatus..."
aws appsync create-resolver \
    --region "$REGION" \
    --api-id "$API_ID" \
    --type-name "Mutation" \
    --field-name "updateEventStatus" \
    --data-source-name "NoneDataSource" \
    --request-mapping-template file://"$RESOLVERS_DIR/Mutation.updateEventStatus.req.vtl" \
    --response-mapping-template file://"$RESOLVERS_DIR/Mutation.updateEventStatus.res.vtl"

echo "--> Attaching Resolver: Query.getOpenIncidents (GSI1-Severity)..."
aws appsync create-resolver \
    --region "$REGION" \
    --api-id "$API_ID" \
    --type-name "Query" \
    --field-name "getOpenIncidents" \
    --data-source-name "DynamoDBDataSource" \
    --request-mapping-template file://"$RESOLVERS_DIR/Query.getOpenIncidents.req.vtl" \
    --response-mapping-template file://"$RESOLVERS_DIR/Query.getOpenIncidents.res.vtl"

echo "--> Attaching Resolver: Query.getRecentEvents..."
aws appsync create-resolver \
    --region "$REGION" \
    --api-id "$API_ID" \
    --type-name "Query" \
    --field-name "getRecentEvents" \
    --data-source-name "DynamoDBDataSource" \
    --request-mapping-template file://"$RESOLVERS_DIR/Query.getRecentEvents.req.vtl" \
    --response-mapping-template file://"$RESOLVERS_DIR/Query.getRecentEvents.res.vtl"

# 8. Retrieve URIs
GRAPHQL_URL=$(aws appsync get-graphql-api \
    --region "$REGION" \
    --api-id "$API_ID" \
    --query "graphqlApi.uris.GRAPHQL" \
    --output text)

REALTIME_URL=$(aws appsync get-graphql-api \
    --region "$REGION" \
    --api-id "$API_ID" \
    --query "graphqlApi.uris.REALTIME" \
    --output text)

echo "=========================================================="
echo "  [Alert Pulse] AppSync API Successfully Deployed! 🚀"
echo "=========================================================="
echo "API ID:       $API_ID"
echo "API Key:      $API_KEY"
echo "GraphQL URL:  $GRAPHQL_URL"
echo "Realtime URL: $REALTIME_URL"
echo "=========================================================="
