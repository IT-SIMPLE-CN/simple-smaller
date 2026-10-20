# 发布用的固定地址与名字，release-mac.sh、publish-mac.sh、publish-windows.sh 共用（被 source，不单独执行）。
# 更新源放自家域名，安装包放对象存储，见 docs/release.md。
# 服务器、目录、公证凭据名等维护者本机的设置不进仓库：写在 .local/private/release.env（同名环境变量优先），
# 要有 DEPLOY_HOST、DEPLOY_REMOTE、NOTARY_KEYCHAIN_PROFILE；发 Windows 版另要 SMALLER_VM、SMALLER_VM_REPO、SMALLER_VM_HOME。
# fork 发布请换成自己的更新源与分发地址（应用里的更新地址也要一起换）。
NAME="简单小一点"
MIN_OS="14.0"
FEED="https://bots.it-simple.cn/downloads/smaller/appcast.xml"
BUCKET="itsimple-bot"
REGION="oss-cn-shenzhen"
PREFIX="smaller"
ASSETS="https://$BUCKET.$REGION.aliyuncs.com/$PREFIX"
RELEASE_ENV="$ROOT/.local/private/release.env"
[ -f "$RELEASE_ENV" ] || { echo "缺少 ${RELEASE_ENV}：发布用的服务器与凭据名写在这里（不进仓库），变量见 scripts/release-env.sh 开头"; exit 1; }
. "$RELEASE_ENV"
HOST="${DEPLOY_HOST:?release.env 里没有 DEPLOY_HOST（放更新源的服务器）}"
REMOTE="${DEPLOY_REMOTE:?release.env 里没有 DEPLOY_REMOTE（服务器上的更新源目录）}"
