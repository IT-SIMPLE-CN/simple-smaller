# 发布用的固定地址与名字，release-mac.sh、publish-mac.sh 共用（被 source，不单独执行）。
# 做法与 dotnfilm-bots 一致：更新源放自家域名，安装包放 OSS，见 docs/release.md。
NAME="简单小一点"
MIN_OS="14.0"
FEED="https://bots.it-simple.cn/downloads/smaller/appcast.xml"
BUCKET="itsimple-bot"
REGION="oss-cn-shenzhen"
PREFIX="smaller"
ASSETS="https://$BUCKET.$REGION.aliyuncs.com/$PREFIX"
HOST="${DEPLOY_HOST:-itsimple-sg}"
REMOTE="/var/www/project/static/downloads/smaller"
